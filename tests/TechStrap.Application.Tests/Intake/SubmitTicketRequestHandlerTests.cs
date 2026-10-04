using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Content;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Intake;
using TechStrap.Domain;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Intake;

public sealed class SubmitTicketRequestHandlerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly ITicketNumberAllocator _allocator = Substitute.For<ITicketNumberAllocator>();
    private readonly IAccessTokenService _tokens = Substitute.For<IAccessTokenService>();
    private readonly IAttachmentStore _attachments = Substitute.For<IAttachmentStore>();
    private readonly IHtmlSanitizer _sanitizer = Substitute.For<IHtmlSanitizer>();
    private readonly IEmailOutbox _outbox = Substitute.For<IEmailOutbox>();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();
    private readonly IIntakeIdempotencyStore _idempotency = Substitute.For<IIntakeIdempotencyStore>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly PortalLinkOptions _portal = new() { PublicUrl = "https://help.test/" };
    private readonly Product _product;
    private readonly Guid _apiKeyId = Guid.CreateVersion7();
    private IUnitOfWork _unitOfWork = UnitOfWorkSubstitute.Create();

    public SubmitTicketRequestHandlerTests()
    {
        _product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        _products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_product);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _allocator.AllocateAsync(_product.Id, Arg.Any<CancellationToken>())
            .Returns(Result<TicketNumber>.Success(TicketNumber.Create("ORB", 42).Value));
        _tokens.Issue(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(call =>
        {
            var token = TicketAccessToken.Issue(call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), "sha256:abc", _clock).Value;
            return DomainResult<IssuedAccessToken>.Ok(new IssuedAccessToken("tok", token));
        });
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _attachments.SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var file = call.Arg<IncomingAttachment>();
            return Result<StoredAttachment>.Success(new StoredAttachment("attachments/x/" + file.FileName, file.FileName, "image/png", file.Length));
        });
    }

    public static TheoryData<string> BadIdempotencyKeys => new() { "", " ", new string('k', 201) };

    private sealed class CapturingLogger : ILogger<SubmitTicketRequestHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private ILogger<SubmitTicketRequestHandler> _logger = NullLogger<SubmitTicketRequestHandler>.Instance;

    private SubmitTicketRequestHandler Handler() =>
        new(_products, _requesters, _tickets, _allocator, _tokens, _attachments, _sanitizer, _outbox, _planner, _idempotency, _unitOfWork, _clock,
            Options.Create(_portal), _logger);

    private static SubmitTicketRequest Request(string? email = "ann@example.com", string? name = "Ann", string? body = "The printer is on fire.", string? externalRef = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(email, name, "Printer", body, externalRef, metadata);

    private static SubmitTicketContext WebContext(IReadOnlyList<IncomingAttachment>? files = null, bool honeypot = false) =>
        new(IntakeChannel.Web, "orbitly", null, null, false, honeypot, files ?? [], null);

    private SubmitTicketContext ApiContext(bool trusted = false, string? idempotencyKey = null, Guid? apiKeyId = null, Guid? productId = null, IReadOnlyList<IncomingAttachment>? files = null) =>
        new(IntakeChannel.Api, null, productId ?? _product.Id, apiKeyId ?? _apiKeyId, trusted, false, files ?? [], idempotencyKey);

    private static IncomingAttachment File(string name, long length = 3) => new(name, "image/png", length, new MemoryStream([1, 2, 3]));

    private static (IUnitOfWork UnitOfWork, IUnitOfWorkScope Scope) SingleScope(params Result[] commits)
    {
        var queue = new Queue<Result>(commits);
        var scope = Substitute.For<IUnitOfWorkScope>();
        scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : Result.Success()));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(scope));
        return (unitOfWork, scope);
    }

    private Ticket ExistingTicket() =>
        Ticket.Create(TicketNumber.Create("ORB", 7).Value, _product.Id, Guid.CreateVersion7(), "Printer", TicketChannel.Api, null, false, _clock).Value;

    private ProductApiKey StoredApiKey()
    {
        var key = ProductApiKey.Restore(_apiKeyId, _product.Id, ApiKeyKind.Trusted, "tsk_abcdefgh", "sha256:feed", null, _clock.GetUtcNow(), null, null);
        _products.GetApiKeyAsync(_apiKeyId, Arg.Any<CancellationToken>()).Returns(key);
        return key;
    }

    [Fact]
    public async Task A_web_submission_creates_requester_ticket_message_token_and_queues_the_confirmation()
    {
        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-42");
        result.Value.ViewUrl.ShouldBeNull();
        result.Value.Warnings.ShouldBeEmpty();
        _requesters.Received(1).Add(Arg.Is<Requester>(r => r.Email == "ann@example.com" && r.Name == "Ann"));
        _tickets.Received(1).Add(Arg.Is<Ticket>(t => t.Channel == TicketChannel.Web && !t.MetadataTrusted && t.PendingMessages.Count == 1));
        _tickets.Received(1).AddAccessToken(Arg.Any<TicketAccessToken>());
        _outbox.Received(1).Enqueue(Arg.Is<EmailOutboxItem>(i =>
            i.Kind == "ticket-confirmation"
            && i.ToAddress == "ann@example.com"
            && i.PayloadJson.Contains("\"portalLink\":\"https://help.test/t/tok\"")));
    }

    [Fact]
    public async Task A_trusted_api_submission_keeps_the_external_ref_and_trusted_metadata_and_returns_the_view_url()
    {
        var metadata = new Dictionary<string, string> { ["plan"] = "pro" };

        var result = await Handler().HandleAsync(Request(externalRef: "u-1", metadata: metadata), ApiContext(trusted: true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ViewUrl.ShouldBe("https://help.test/t/tok");
        result.Value.Warnings.ShouldBeEmpty();
        _requesters.Received(1).Add(Arg.Is<Requester>(r => r.ExternalUserRef == "u-1"));
        _tickets.Received(1).Add(Arg.Is<Ticket>(t => t.Channel == TicketChannel.Api && t.MetadataTrusted && t.MetadataJson!.Contains("pro")));
    }

    [Theory]
    [InlineData(IntakeChannel.Web)]
    [InlineData(IntakeChannel.Api)]
    public async Task An_untrusted_submission_drops_the_external_ref_with_a_warning_and_marks_metadata_untrusted(IntakeChannel channel)
    {
        var context = channel == IntakeChannel.Web ? WebContext() : ApiContext(trusted: false);
        var metadata = new Dictionary<string, string> { ["plan"] = "pro" };

        var result = await Handler().HandleAsync(Request(externalRef: "u-1", metadata: metadata), context, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldBe(["external-user-ref-ignored"]);
        _requesters.Received(1).Add(Arg.Is<Requester>(r => r.ExternalUserRef == null));
        _tickets.Received(1).Add(Arg.Is<Ticket>(t => !t.MetadataTrusted));
    }

    [Theory]
    [InlineData(null, "Ann", "Ann")]
    [InlineData("Annie", "Ann", "Annie")]
    public async Task An_existing_requester_is_reused_case_insensitively_and_only_blank_names_are_filled(string? existingName, string requestName, string expectedName)
    {
        var existing = Requester.Restore(Guid.CreateVersion7(), "ann@example.com", existingName, null, null, 1);
        _requesters.GetByEmailAsync("ANN@example.com", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Handler().HandleAsync(Request(email: "ANN@example.com", name: requestName), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.Name.ShouldBe(expectedName);
        _requesters.DidNotReceive().Add(Arg.Any<Requester>());
        _requesters.Received(existingName is null ? 1 : 0).Update(existing);
        _tickets.Received(1).Add(Arg.Is<Ticket>(t => t.RequesterId == existing.Id));
    }

    [Fact]
    public async Task An_untrusted_submitter_never_overwrites_a_known_external_ref()
    {
        var existing = Requester.Restore(Guid.CreateVersion7(), "ann@example.com", "Ann", "u-9", null, 1);
        _requesters.GetByEmailAsync("ann@example.com", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Handler().HandleAsync(Request(externalRef: "attacker"), ApiContext(trusted: false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.ExternalUserRef.ShouldBe("u-9");
        _requesters.DidNotReceive().Update(Arg.Any<Requester>());
    }

    [Fact]
    public async Task A_trusted_submitter_replaces_the_external_ref_only_when_one_is_sent()
    {
        var existing = Requester.Restore(Guid.CreateVersion7(), "ann@example.com", "Ann", "u-9", null, 1);
        _requesters.GetByEmailAsync("ann@example.com", Arg.Any<CancellationToken>()).Returns(existing);

        (await Handler().HandleAsync(Request(externalRef: null), ApiContext(trusted: true), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        existing.ExternalUserRef.ShouldBe("u-9");

        (await Handler().HandleAsync(Request(externalRef: "u-10"), ApiContext(trusted: true), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        existing.ExternalUserRef.ShouldBe("u-10");
        _requesters.Received(1).Update(existing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_or_inactive_product_is_not_found(bool exists)
    {
        if (exists)
        {
            _product.SetActive(false);
        }
        else
        {
            _products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns((Product?)null);
        }

        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("product-not-found");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_honeypot_hit_returns_a_normal_looking_success_and_writes_nothing()
    {
        _unitOfWork = Substitute.For<IUnitOfWork>();

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png")], honeypot: true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldStartWith("ORB-");
        result.Value.ViewUrl.ShouldBeNull();
        result.Value.Warnings.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<EmailOutboxItem>());
        _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
        _requesters.DidNotReceive().Add(Arg.Any<Requester>());
        await _attachments.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task More_than_five_files_or_more_than_25_mib_in_total_is_a_field_error_before_anything_is_stored()
    {
        var sixFiles = Enumerable.Range(0, 6).Select(i => File($"f{i}.png", 1)).ToList();
        var tooMany = await Handler().HandleAsync(Request(), WebContext(sixFiles), TestContext.Current.CancellationToken);

        var twoBigFiles = new[] { File("a.png", 13L * 1024 * 1024), File("b.png", 13L * 1024 * 1024) };
        var tooLarge = await Handler().HandleAsync(Request(), WebContext(twoBigFiles), TestContext.Current.CancellationToken);

        tooMany.Errors[0].Code.ShouldBe("attachments-too-many");
        tooMany.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        tooMany.Errors[0].Target.ShouldBe("attachments");
        tooLarge.Errors[0].Code.ShouldBe("attachments-too-large");
        tooLarge.Errors[0].Target.ShouldBe("attachments");
        await _attachments.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bad_metadata_is_a_field_error()
    {
        var tooManyKeys = Enumerable.Range(0, 51).ToDictionary(i => $"k{i}", _ => "v");
        var cases = new IReadOnlyDictionary<string, string>[]
        {
            tooManyKeys,
            new Dictionary<string, string> { [" "] = "v" },
            new Dictionary<string, string> { [new string('k', 65)] = "v" },
            new Dictionary<string, string> { ["k"] = new string('v', 1_001) },
        };

        foreach (var metadata in cases)
        {
            var result = await Handler().HandleAsync(Request(metadata: metadata), ApiContext(), TestContext.Current.CancellationToken);

            result.Errors[0].Code.ShouldBe("metadata-invalid");
            result.Errors[0].Target.ShouldBe("metadata");
        }

        _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
    }

    [Fact]
    public async Task A_rejected_attachment_removes_files_already_stored_and_saves_nothing()
    {
        var (unitOfWork, scope) = SingleScope();
        _unitOfWork = unitOfWork;
        var second = File("b.png");
        _attachments.SaveAsync(Arg.Any<Guid>(), second, Arg.Any<CancellationToken>()).Returns(
            Result<StoredAttachment>.Failure(new ResultError("attachment-empty", "The file is empty.", ResultErrorKind.Validation, "attachments")));

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png"), second]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("attachment-empty");
        await _attachments.Received(1).DeleteAsync("attachments/x/a.png", Arg.Any<CancellationToken>());
        await scope.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
    }

    [Fact]
    public async Task A_failed_commit_deletes_the_stored_attachments()
    {
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict"));

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png"), File("b.png")]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        await _attachments.Received(1).DeleteAsync("attachments/x/a.png", Arg.Any<CancellationToken>());
        await _attachments.Received(1).DeleteAsync("attachments/x/b.png", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_attachment_cleanup_is_logged_and_the_original_failure_is_returned()
    {
        var logger = new CapturingLogger();
        _logger = logger;
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict"));
        _attachments.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disk"));

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png")]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("concurrency-conflict");
        logger.Messages.ShouldContain(m => m.StartsWith("Warning: Attachment cleanup failed for attachments/x/a.png (IOException)"));
    }

    [Fact]
    public async Task A_disposal_failure_after_a_successful_commit_does_not_delete_the_committed_files()
    {
        var scope = Substitute.For<IUnitOfWorkScope>();
        scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));
        scope.DisposeAsync().Returns(_ => ValueTask.FromException(new InvalidOperationException("dispose")));
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(scope));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Handler().HandleAsync(Request(), WebContext([File("a.png")]), TestContext.Current.CancellationToken));

        await _attachments.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_exception_removes_the_files_the_attempt_stored_and_propagates()
    {
        _tickets.When(t => t.AddAccessToken(Arg.Any<TicketAccessToken>())).Throw(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Handler().HandleAsync(Request(), WebContext([File("a.png")]), TestContext.Current.CancellationToken));

        await _attachments.Received(1).DeleteAsync("attachments/x/a.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_body_is_plain_text_turned_into_sanitised_html()
    {
        var result = await Handler().HandleAsync(Request(body: "<b>hi</b>"), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _sanitizer.Received(1).Sanitize("<p>&lt;b&gt;hi&lt;/b&gt;</p>");
    }

    [Fact]
    public async Task A_repeated_idempotency_key_returns_the_original_ticket_with_a_fresh_link_and_creates_nothing_else()
    {
        var (unitOfWork, scope) = SingleScope();
        _unitOfWork = unitOfWork;
        var ticket = ExistingTicket();
        var stored = JsonSerializer.Serialize(new SubmitTicketResponse("ORB-7", null, []), Json);
        _idempotency.FindAsync(_apiKeyId, "k1", Arg.Any<CancellationToken>())
            .Returns(new IntakeIdempotencyEntry(Guid.CreateVersion7(), _apiKeyId, ticket.Id, stored, _clock.GetUtcNow().AddHours(-1)));
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);

        var result = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: "k1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-7");
        result.Value.ViewUrl.ShouldBe("https://help.test/t/tok");
        _tickets.Received(1).AddAccessToken(Arg.Is<TicketAccessToken>(t => t.TicketId == ticket.Id && t.RequesterId == ticket.RequesterId));
        await scope.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _allocator.DidNotReceive().AllocateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
        _requesters.DidNotReceive().Add(Arg.Any<Requester>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<EmailOutboxItem>());
        _products.DidNotReceive().UpdateApiKey(Arg.Any<ProductApiKey>());
    }

    [Fact]
    public async Task The_stored_idempotent_response_never_holds_the_link()
    {
        var result = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: "k1"), TestContext.Current.CancellationToken);

        result.Value.ViewUrl.ShouldBe("https://help.test/t/tok");
        _idempotency.Received(1).Add(
            _apiKeyId,
            "k1",
            Arg.Any<Guid>(),
            Arg.Is<string>(json => !json.Contains("/t/") && !json.Contains("tok") && json.Contains("\"viewUrl\":null")),
            _clock.GetUtcNow());
        await _idempotency.Received(1).PruneAsync(_clock.GetUtcNow() - IIntakeIdempotencyStore.Retention, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_prune_failure_never_fails_the_submission()
    {
        _idempotency.PruneAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: "k1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_expired_idempotency_key_is_replaced()
    {
        var entry = new IntakeIdempotencyEntry(Guid.CreateVersion7(), _apiKeyId, Guid.CreateVersion7(), "{}", _clock.GetUtcNow().AddHours(-25));
        _idempotency.FindAsync(_apiKeyId, "k1", Arg.Any<CancellationToken>()).Returns(entry);

        var result = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: "k1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-42");
        _idempotency.Received(1).Remove(entry);
        _idempotency.Received(1).Add(_apiKeyId, "k1", Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>());
        _tickets.Received(1).Add(Arg.Any<Ticket>());
        await _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(BadIdempotencyKeys))]
    public async Task A_blank_or_over_long_idempotency_key_is_a_field_error(string key)
    {
        var result = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: key), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("idempotency-key-invalid");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        result.Errors[0].Target.ShouldBe("Idempotency-Key");
    }

    [Fact]
    public async Task A_body_over_the_domain_limit_is_rejected_before_it_reaches_the_sanitizer()
    {
        var result = await Handler().HandleAsync(Request(body: new string('x', 100_001)), ApiContext(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("body-too-long");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        result.Errors[0].Target.ShouldBe("body");
        _sanitizer.DidNotReceive().Sanitize(Arg.Any<string>());
    }

    [Fact]
    public async Task An_api_submission_without_a_key_principal_is_unauthenticated()
    {
        var context = new SubmitTicketContext(IntakeChannel.Api, null, null, null, false, false, [], null);

        var result = await Handler().HandleAsync(Request(), context, TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("api-key-required");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Unauthenticated);
    }

    [Fact]
    public async Task A_queueing_failure_never_fails_the_submission()
    {
        _portal.PublicUrl = "https://help.test/" + new string('a', 16_000);

        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _outbox.DidNotReceive().Enqueue(Arg.Any<EmailOutboxItem>());
    }

    [Fact]
    public async Task A_new_ticket_plans_the_new_ticket_alert_once()
    {
        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _planner.Received(1).PlanNewTicketAsync(
            Arg.Is<Ticket>(t => t.Subject == "Printer"),
            Arg.Is<Requester>(r => r.Email == "ann@example.com"),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_honeypot_and_an_idempotent_replay_plan_no_alert()
    {
        await Handler().HandleAsync(Request(), WebContext(honeypot: true), TestContext.Current.CancellationToken);

        var ticket = ExistingTicket();
        var stored = JsonSerializer.Serialize(new SubmitTicketResponse("ORB-7", null, []), Json);
        _idempotency.FindAsync(_apiKeyId, "k1", Arg.Any<CancellationToken>())
            .Returns(new IntakeIdempotencyEntry(Guid.CreateVersion7(), _apiKeyId, ticket.Id, stored, _clock.GetUtcNow().AddHours(-1)));
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var replay = await Handler().HandleAsync(Request(), ApiContext(idempotencyKey: "k1"), TestContext.Current.CancellationToken);

        replay.IsSuccess.ShouldBeTrue();
        await _planner.DidNotReceive().PlanNewTicketAsync(Arg.Any<Ticket>(), Arg.Any<Requester>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_retried_attempt_plans_the_alert_in_the_attempt_that_commits()
    {
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate"), Result.Success());

        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _planner.Received(2).PlanNewTicketAsync(Arg.Any<Ticket>(), Arg.Any<Requester>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_duplicate_conflict_on_the_first_attempt_is_retried_once()
    {
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate"), Result.Success());

        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.Received(2).BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_second_duplicate_conflict_is_returned()
    {
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate"), UnitOfWorkSubstitute.Conflict("duplicate"));

        var result = await Handler().HandleAsync(Request(), WebContext(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("duplicate");
        await _unitOfWork.Received(2).BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_duplicate_conflict_with_attachments_is_not_retried()
    {
        _unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate"));

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png")]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("duplicate");
        await _unitOfWork.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await _attachments.Received(1).DeleteAsync("attachments/x/a.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_api_key_use_is_recorded()
    {
        var key = StoredApiKey();

        var result = await Handler().HandleAsync(Request(), ApiContext(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        key.LastUsedAt.ShouldBe(_clock.GetUtcNow());
        _products.Received(1).UpdateApiKey(key);
    }

    [Fact]
    public async Task Cancellation_reaches_the_repositories_store_and_allocator()
    {
        using var source = new CancellationTokenSource();
        var token = source.Token;

        var result = await Handler().HandleAsync(Request(), WebContext([File("a.png")]), token);

        result.IsSuccess.ShouldBeTrue();
        await _allocator.Received(1).AllocateAsync(_product.Id, token);
        await _attachments.Received(1).SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), token);
        await _requesters.Received(1).GetByEmailAsync("ann@example.com", token);
        await _products.Received(1).GetByKeyAsync("orbitly", token);
        await _unitOfWork.Received(1).BeginAsync(token);
    }
}
