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
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class AddCustomerReplyRequestHandlerTests
{
    private const string Raw = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly IAccessTokenService _tokens = Substitute.For<IAccessTokenService>();
    private readonly ITicketRepository _tickets = TicketRepositorySubstitute.Create();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly ITicketNumberAllocator _allocator = Substitute.For<ITicketNumberAllocator>();
    private readonly IAttachmentStore _store = Substitute.For<IAttachmentStore>();
    private readonly IHtmlSanitizer _sanitizer = Substitute.For<IHtmlSanitizer>();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();
    private readonly PortalLinkOptions _portal = new() { PublicUrl = "https://help.test/" };
    private readonly Requester _ann;
    private readonly Guid _productId = Guid.NewGuid();
    private int _fileCounter;

    public AddCustomerReplyRequestHandlerTests()
    {
        _ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns(_ann);
        _tokens.Hash(Raw).Returns("sha256:abc");
        _tokens.Issue(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(call =>
        {
            var token = TicketAccessToken.Issue(call.ArgAt<Guid>(0), call.ArgAt<Guid>(1), "sha256:new", _clock).Value;
            return DomainResult<IssuedAccessToken>.Ok(new IssuedAccessToken("fresh-token", token));
        });
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _allocator.AllocateAsync(_productId, Arg.Any<CancellationToken>())
            .Returns(Result<TicketNumber>.Success(TicketNumber.Create("ORB", 77).Value));
        _store.SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var file = call.Arg<IncomingAttachment>();
            return Result<StoredAttachment>.Success(new StoredAttachment($"key-{++_fileCounter}", file.FileName, "image/png", file.Length));
        });
        _tickets.ListRecentFollowUpsAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private Ticket GivenTicket(TicketStatus status, bool spam = false)
    {
        var ticket = TicketBuilder.New(_clock, _productId, _ann.Id);
        var actor = Actor.ForAgent(Guid.NewGuid());
        if (spam)
        {
            ticket.MarkSpam(true, actor, _clock).IsSuccess.ShouldBeTrue();
        }

        TicketStatus[] path = status switch
        {
            TicketStatus.New => [],
            TicketStatus.Open => [TicketStatus.Open],
            TicketStatus.Pending => [TicketStatus.Open, TicketStatus.Pending],
            TicketStatus.Solved => [TicketStatus.Solved],
            _ => [TicketStatus.Solved, TicketStatus.Closed],
        };
        foreach (var step in path)
        {
            ticket.ChangeStatus(step, actor, _clock).IsSuccess.ShouldBeTrue();
        }

        ticket.AcceptChanges();
        var token = TicketAccessToken.Issue(ticket.Id, _ann.Id, "sha256:abc", _clock).Value;
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(token);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        return ticket;
    }

    private AddCustomerReplyRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_tokens, _tickets, _requesters, _allocator, _store, _sanitizer, _planner, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock,
            Options.Create(_portal), NullLogger<AddCustomerReplyRequestHandler>.Instance);

    private Task<Result<CustomerReplyResponse>> Reply(
        string? body = "Still broken", IReadOnlyList<IncomingAttachment>? files = null, IUnitOfWork? unitOfWork = null, string? token = Raw, CancellationToken? ct = null) =>
        Handler(unitOfWork).HandleAsync(token, new AddCustomerReplyRequest(body), files ?? [], ct ?? Ct);

    private static IncomingAttachment Png(string name = "shot.png", long length = 12) => new(name, "image/png", length, new MemoryStream([1, 2, 3]));

    private static Result Conflict() => UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict);

    [Theory]
    [InlineData(TicketStatus.Pending)]
    [InlineData(TicketStatus.Solved)]
    public async Task A_reply_on_pending_or_solved_reopens_and_alerts_with_reopened_true(TicketStatus status)
    {
        var ticket = GivenTicket(status);

        var result = await Reply();

        result.IsSuccess.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Open);
        result.Value.ShouldSatisfyAllConditions(
            r => r.TicketNumber.ShouldBe(ticket.Number.ToString()),
            r => r.FollowUpCreated.ShouldBeFalse(),
            r => r.FollowUpViewUrl.ShouldBeNull());
        _tickets.Received(1).Update(ticket);
        await _planner.Received(1).PlanCustomerReplyAsync(ticket, true, Ct);
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    public async Task A_reply_on_new_or_open_keeps_the_status_and_alerts_with_reopened_false(TicketStatus status)
    {
        var ticket = GivenTicket(status);

        var result = await Reply();

        result.IsSuccess.ShouldBeTrue();
        ticket.Status.ShouldBe(status);
        await _planner.Received(1).PlanCustomerReplyAsync(ticket, false, Ct);
        _tickets.Received(1).UpdateAccessToken(Arg.Any<TicketAccessToken>());
    }

    [Fact]
    public async Task The_reply_body_is_plain_text_escaped_then_sanitised()
    {
        GivenTicket(TicketStatus.Open);

        var result = await Reply("<b>hi</b>\nthere");

        result.IsSuccess.ShouldBeTrue();
        _sanitizer.Received(1).Sanitize("<p>&lt;b&gt;hi&lt;/b&gt;<br>there</p>");
    }

    [Fact]
    public async Task A_bad_token_is_the_uniform_not_found()
    {
        GivenTicket(TicketStatus.Open);

        var result = await Reply(token: "wrong-token");

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.NotFound),
            e => e.Message.ShouldBe("Not found."));
        _tickets.DidNotReceive().Update(Arg.Any<Ticket>());
    }

    [Fact]
    public async Task A_reply_on_a_closed_ticket_creates_a_follow_up_with_a_new_number_and_link()
    {
        var parent = GivenTicket(TicketStatus.Closed);
        Ticket? added = null;
        _tickets.When(t => t.Add(Arg.Any<Ticket>())).Do(call => added = call.Arg<Ticket>());

        var result = await Reply("It broke again");

        result.IsSuccess.ShouldBeTrue();
        added.ShouldNotBeNull();
        added.ParentTicketId.ShouldBe(parent.Id);
        added.Number.ToString().ShouldBe("ORB-77");
        parent.Status.ShouldBe(TicketStatus.Closed);
        _tickets.Received(1).Update(parent);
        _tickets.Received(1).AddAccessToken(Arg.Is<TicketAccessToken>(t => t.TicketId == added.Id && t.RequesterId == _ann.Id));
        await _planner.Received(1).PlanFollowUpConfirmationAsync(added, Ct);
        await _planner.Received(1).PlanNewTicketAsync(added, _ann, true, Ct);
        await _planner.DidNotReceive().PlanCustomerReplyAsync(Arg.Any<Ticket>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        result.Value.ShouldSatisfyAllConditions(
            r => r.TicketNumber.ShouldBe("ORB-77"),
            r => r.FollowUpCreated.ShouldBeTrue(),
            r => r.FollowUpViewUrl.ShouldBe("https://help.test/t/fresh-token"));
    }

    [Fact]
    public async Task The_same_text_within_two_minutes_replays_the_existing_follow_up()
    {
        var parent = GivenTicket(TicketStatus.Closed);
        var followUpId = Guid.NewGuid();
        var firstMessageId = Guid.NewGuid();
        _tickets.ListRecentFollowUpsAsync(parent.Id, _clock.GetUtcNow() - AddCustomerReplyRequestHandler.FollowUpDedupeWindow, Arg.Any<CancellationToken>())
            .Returns([new FollowUpCandidate(followUpId, "ORB-70", firstMessageId, "<p>It broke again</p>", _clock.GetUtcNow())]);

        var result = await Reply("It broke again", [Png()]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new CustomerReplyResponse("ORB-70", firstMessageId, true, "https://help.test/t/fresh-token"));
        _tickets.Received(1).AddAccessToken(Arg.Is<TicketAccessToken>(t => t.TicketId == followUpId));
        await _allocator.DidNotReceive().AllocateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
        _tickets.DidNotReceive().Update(Arg.Any<Ticket>());
        await _store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>());
        await _planner.DidNotReceiveWithAnyArgs().PlanFollowUpConfirmationAsync(default!, Ct);
        await _planner.DidNotReceiveWithAnyArgs().PlanNewTicketAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task Different_text_or_an_older_follow_up_creates_a_new_one()
    {
        var parent = GivenTicket(TicketStatus.Closed);
        _tickets.ListRecentFollowUpsAsync(parent.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new FollowUpCandidate(Guid.NewGuid(), "ORB-70", Guid.NewGuid(), "<p>Something else</p>", _clock.GetUtcNow())]);

        var different = await Reply("It broke again");

        different.Value.FollowUpCreated.ShouldBeTrue();
        different.Value.TicketNumber.ShouldBe("ORB-77");

        // The window is the cut-off passed to the repository: an older follow-up is simply not returned.
        parent = GivenTicket(TicketStatus.Closed);
        _tickets.ListRecentFollowUpsAsync(parent.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _clock.Advance(TimeSpan.FromMinutes(3));
        (await Reply("It broke again")).Value.FollowUpCreated.ShouldBeTrue();
        await _tickets.Received().ListRecentFollowUpsAsync(parent.Id, _clock.GetUtcNow() - TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>());
        await _allocator.Received(2).AllocateAsync(_productId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_commit_deletes_stored_files_and_a_conflict_retries_once()
    {
        GivenTicket(TicketStatus.Open);

        // Files present: the conflict is not retried, and the reply conflict is returned after the files are cleaned up.
        var withFiles = await Reply(files: [Png()], unitOfWork: UnitOfWorkSubstitute.Create(Conflict()));
        withFiles.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Code.ShouldBe("reply-conflict"),
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict));
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);

        // No files: one conflict, then the retry succeeds.
        var retryUow = UnitOfWorkSubstitute.Create(Conflict());
        var retried = await Reply(unitOfWork: retryUow);
        await retryUow.Received(2).BeginAsync(Arg.Any<CancellationToken>());
        retried.IsSuccess.ShouldBeTrue();

        // Two conflicts in a row give up with the reply conflict.
        var twiceUow = UnitOfWorkSubstitute.Create(Conflict(), Conflict());
        var twice = await Reply(unitOfWork: twiceUow);
        await twiceUow.Received(2).BeginAsync(Arg.Any<CancellationToken>());
        twice.Errors.ShouldHaveSingleItem().Code.ShouldBe("reply-conflict");

        // Any other failure is returned as is, and a reference violation means the ticket is gone.
        var other = await Reply(files: [Png()], unitOfWork: UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate")));
        other.Errors.ShouldHaveSingleItem().Code.ShouldBe("duplicate");
        await _store.Received(1).DeleteAsync("key-2", CancellationToken.None);
        var gone = await Reply(unitOfWork: UnitOfWorkSubstitute.Create(Result.Failure(new ResultError("reference-violation", "x", ResultErrorKind.Conflict))));
        gone.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_conflict_with_files_on_a_closed_ticket_replays_a_follow_up_found_in_a_fresh_scope()
    {
        var parent = GivenTicket(TicketStatus.Closed);
        var winner = new FollowUpCandidate(Guid.NewGuid(), "ORB-70", Guid.NewGuid(), "<p>Boom</p>", _clock.GetUtcNow());
        _tickets.ListRecentFollowUpsAsync(parent.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([], [winner]);

        var result = await Reply("Boom", [Png()], UnitOfWorkSubstitute.Create(Conflict()));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-70");
        result.Value.FollowUpCreated.ShouldBeTrue();
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
        _tickets.Received(2).UpdateAccessToken(Arg.Any<TicketAccessToken>()); // the failed attempt, then the fresh-scope replay slides the expiry too
    }

    [Fact]
    public async Task A_follow_up_of_a_spam_ticket_is_spam_and_alerts_nobody_but_still_returns_the_link()
    {
        var parent = GivenTicket(TicketStatus.Closed, spam: true);
        Ticket? added = null;
        _tickets.When(t => t.Add(Arg.Any<Ticket>())).Do(call => added = call.Arg<Ticket>());

        var result = await Reply("It broke again");

        result.IsSuccess.ShouldBeTrue();
        added.ShouldNotBeNull().IsSpam.ShouldBeTrue();
        added.ParentTicketId.ShouldBe(parent.Id);
        result.Value.ShouldSatisfyAllConditions(
            r => r.FollowUpCreated.ShouldBeTrue(),
            r => r.FollowUpViewUrl.ShouldBe("https://help.test/t/fresh-token"));
        await _planner.DidNotReceiveWithAnyArgs().PlanNewTicketAsync(default!, default!, default, Ct);
        await _planner.DidNotReceiveWithAnyArgs().PlanCustomerReplyAsync(default!, default, Ct);
    }

    [Fact]
    public async Task A_thrown_exception_deletes_stored_files()
    {
        GivenTicket(TicketStatus.Open);
        _planner.PlanCustomerReplyAsync(Arg.Any<Ticket>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => Reply(files: [Png()]));

        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
    }

    [Fact]
    public async Task Too_many_files_or_an_over_long_body_is_rejected_before_any_lookup()
    {
        GivenTicket(TicketStatus.Open);

        var tooLong = await Reply(new string('x', DomainLimits.MessageBodyMaxLength + 1));
        var tooMany = await Reply(files: [.. Enumerable.Range(0, IntakeLimits.MaxFiles + 1).Select(_ => Png())]);
        var tooLarge = await Reply(files: [Png(length: IntakeLimits.MaxMessageBytes + 1)], token: null);

        tooLong.Errors.ShouldHaveSingleItem().Code.ShouldBe("body-too-long");
        tooMany.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachments-too-many");
        tooLarge.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachments-too-large");
        await _tickets.DidNotReceiveWithAnyArgs().GetAccessTokenByHashAsync(default!, Ct);
    }

    [Fact]
    public async Task An_empty_body_is_the_domain_body_required_validation()
    {
        GivenTicket(TicketStatus.Open);

        var result = await Reply("   ");

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.Validation);
    }

    [Fact]
    public async Task Cancellation_reaches_the_repositories_store_and_planner()
    {
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var open = GivenTicket(TicketStatus.Open);

        (await Reply(files: [Png()], ct: token)).IsSuccess.ShouldBeTrue();
        await _tickets.Received().GetAccessTokenByHashAsync("sha256:abc", token);
        await _tickets.Received().GetByIdAsync(open.Id, token);
        await _requesters.Received().GetByIdAsync(_ann.Id, token);
        await _store.Received().SaveAsync(open.Id, Arg.Any<IncomingAttachment>(), token);
        await _planner.Received().PlanCustomerReplyAsync(open, false, token);

        var closed = GivenTicket(TicketStatus.Closed);
        (await Reply(files: [Png()], ct: token)).IsSuccess.ShouldBeTrue();
        await _tickets.Received().ListRecentFollowUpsAsync(closed.Id, Arg.Any<DateTimeOffset>(), token);
        await _allocator.Received().AllocateAsync(_productId, token);
        await _planner.Received().PlanFollowUpConfirmationAsync(Arg.Any<Ticket>(), token);
        await _planner.Received().PlanNewTicketAsync(Arg.Any<Ticket>(), _ann, true, token);
    }
}
