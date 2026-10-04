using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Requesters;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class RequestNewAccessLinkRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly ITicketRepository _tickets = TicketRepositorySubstitute.Create();
    private readonly IEmailOutboxStore _outbox = Substitute.For<IEmailOutboxStore>();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();
    private readonly LostLinkOptions _options = new();
    private readonly Requester _ann;
    private readonly RequesterTicketLink[] _links;

    public RequestNewAccessLinkRequestHandlerTests()
    {
        _ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _requesters.GetByEmailAsync("ann@example.com", Arg.Any<CancellationToken>()).Returns(_ann);
        _links = [new RequesterTicketLink(Guid.NewGuid(), Guid.NewGuid(), "ORB-1", "Login", _clock.GetUtcNow())];
        _tickets.ListRecentTicketsForRequesterAsync(_ann.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_links);
        _outbox.CountRecentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
    }

    private readonly CapturingLogger _logger = new();

    private sealed class CapturingLogger : ILogger<RequestNewAccessLinkRequestHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private RequestNewAccessLinkRequestHandler Handler(IUnitOfWork unitOfWork) =>
        new(_requesters, _tickets, _outbox, _planner, unitOfWork, _clock, Options.Create(_options), _logger);

    private Task<Result> Request(string? email, IUnitOfWork? unitOfWork = null) =>
        Handler(unitOfWork ?? UnitOfWorkSubstitute.Create()).HandleAsync(new RequestNewAccessLinkRequest(email), Ct);

    [Fact]
    public async Task A_known_requester_gets_one_access_links_email()
    {
        var unitOfWork = UnitOfWorkSubstitute.Create();

        var result = await Request("  Ann@Example.com ", unitOfWork);

        result.IsSuccess.ShouldBeTrue();
        await _tickets.Received(1).ListRecentTicketsForRequesterAsync(_ann.Id, _options.MaxLinks, Ct);
        await unitOfWork.Received(1).BeginAsync(Ct);
        await _planner.Received(1).PlanAccessLinksAsync(_ann, _links, Ct);
        await _outbox.Received(1).CountRecentAsync(
            EmailTemplates.AccessLinks, "ann@example.com", _clock.GetUtcNow().AddMinutes(-_options.PerAddressWindowMinutes), Ct);
    }

    [Fact]
    public async Task An_unknown_or_erased_address_returns_success_and_plans_nothing()
    {
        var gone = Requester.Create("gone@example.com", "Gone", null, _clock).Value;
        gone.Erase(_clock);
        _requesters.GetByEmailAsync("gone@example.com", Arg.Any<CancellationToken>()).Returns(gone);
        var unitOfWork = UnitOfWorkSubstitute.Create();

        (await Request("nobody@example.com", unitOfWork)).IsSuccess.ShouldBeTrue();
        (await Request("gone@example.com", unitOfWork)).IsSuccess.ShouldBeTrue();

        await _planner.DidNotReceiveWithAnyArgs().PlanAccessLinksAsync(default!, default!, Ct);
        await unitOfWork.DidNotReceiveWithAnyArgs().BeginAsync(Ct);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task Over_the_per_address_cap_nothing_is_sent_and_the_answer_is_the_same(int recent, bool sends)
    {
        _outbox.CountRecentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(recent);
        var unitOfWork = UnitOfWorkSubstitute.Create();

        var result = await Request("ann@example.com", unitOfWork);

        result.IsSuccess.ShouldBeTrue();
        await _requesters.Received(1).GetByEmailAsync("ann@example.com", Ct);
        if (sends)
        {
            await _planner.Received(1).PlanAccessLinksAsync(_ann, _links, Ct);
        }
        else
        {
            await _tickets.DidNotReceiveWithAnyArgs().ListRecentTicketsForRequesterAsync(default, default, Ct);
            await _planner.DidNotReceiveWithAnyArgs().PlanAccessLinksAsync(default!, default!, Ct);
            await unitOfWork.DidNotReceiveWithAnyArgs().BeginAsync(Ct);
        }
    }

    [Fact]
    public async Task A_known_requester_with_no_tickets_returns_success_and_plans_nothing()
    {
        _tickets.ListRecentTicketsForRequesterAsync(_ann.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var unitOfWork = UnitOfWorkSubstitute.Create();

        (await Request("ann@example.com", unitOfWork)).IsSuccess.ShouldBeTrue();

        await unitOfWork.DidNotReceiveWithAnyArgs().BeginAsync(Ct);
        await _planner.DidNotReceiveWithAnyArgs().PlanAccessLinksAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_planner_fault_still_returns_success_and_logs_only_the_exception_type()
    {
        _planner.PlanAccessLinksAsync(Arg.Any<Requester>(), Arg.Any<IReadOnlyList<RequesterTicketLink>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("secret ann@example.com detail"));

        var result = await Request("ann@example.com");

        result.IsSuccess.ShouldBeTrue();
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(nameof(InvalidOperationException));
        entry.Message.ShouldNotContain("ann");
        entry.Message.ShouldNotContain("secret");
    }

    [Fact]
    public async Task A_cancelled_request_still_propagates_cancellation()
    {
        using var cts = new CancellationTokenSource();
        _planner.PlanAccessLinksAsync(Arg.Any<Requester>(), Arg.Any<IReadOnlyList<RequesterTicketLink>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Should.ThrowAsync<OperationCanceledException>(
            Handler(UnitOfWorkSubstitute.Create()).HandleAsync(new RequestNewAccessLinkRequest("ann@example.com"), cts.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task A_malformed_address_is_400_email_invalid(string? email)
    {
        var result = await Request(email);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("email-invalid");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        await _outbox.DidNotReceiveWithAnyArgs().CountRecentAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task The_count_and_lookup_run_for_every_well_formed_address()
    {
        await Request("nobody@example.com");
        await Request("ann@example.com");

        await _outbox.Received(1).CountRecentAsync(EmailTemplates.AccessLinks, "nobody@example.com", Arg.Any<DateTimeOffset>(), Ct);
        await _outbox.Received(1).CountRecentAsync(EmailTemplates.AccessLinks, "ann@example.com", Arg.Any<DateTimeOffset>(), Ct);
        await _requesters.Received(1).GetByEmailAsync("nobody@example.com", Ct);
        await _requesters.Received(1).GetByEmailAsync("ann@example.com", Ct);
    }

    [Fact]
    public async Task A_failed_commit_still_returns_success()
    {
        var result = await Request("ann@example.com", UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("duplicate")));

        result.IsSuccess.ShouldBeTrue();
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("duplicate");
        entry.Message.ShouldNotContain("ann");
        entry.Message.ShouldNotContain("example");
    }
}
