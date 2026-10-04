using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class AutoCloseSolvedTicketsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly List<(Guid TicketId, TicketEventType Type, ActorType Actor)> _staged = [];
    private readonly ITicketRepository _tickets;
    private readonly AutoCloseOptions _options = new() { Days = 7, BatchSize = 50 };

    public AutoCloseSolvedTicketsHandlerTests() =>
        _tickets = TicketRepositorySubstitute.Create(t => _staged.AddRange(t.PendingEvents.Select(e => (t.Id, e.Type, e.ActorType))));

    private AutoCloseSolvedTicketsHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_tickets, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock, Options.Create(_options), NullLogger<AutoCloseSolvedTicketsHandler>.Instance);

    private Ticket Solved()
    {
        var ticket = TicketBuilder.InStatus(TicketStatus.Solved, _clock);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        return ticket;
    }

    private void Candidates(params Ticket[] tickets) =>
        _tickets.ListSolvedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(tickets);

    [Fact]
    public async Task Tickets_solved_at_least_n_days_ago_are_closed_by_the_system()
    {
        var ticket = Solved();
        Candidates(ticket);
        _clock.Advance(TimeSpan.FromDays(8));

        var result = await Handler().HandleAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AutoCloseResult(1, 1, 0));
        ticket.Status.ShouldBe(TicketStatus.Closed);
        ticket.ClosedAt.ShouldNotBeNull();
        _staged.ShouldBe([(ticket.Id, TicketEventType.StatusChanged, ActorType.System)]);
        _tickets.Received(1).Update(ticket);
        await _tickets.Received(1).ListSolvedBeforeAsync(_clock.GetUtcNow() - TimeSpan.FromDays(7), 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Spam_reopened_or_recently_solved_candidates_are_skipped()
    {
        var spam = Solved();
        spam.MarkSpam(true, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        var reopened = Solved();
        reopened.ChangeStatus(TicketStatus.Open, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        var missing = Solved();
        _tickets.GetByIdAsync(missing.Id, Arg.Any<CancellationToken>()).Returns((Ticket?)null);
        _clock.Advance(TimeSpan.FromDays(8));
        var recent = Solved();
        spam.AcceptChanges();
        reopened.AcceptChanges();
        Candidates(spam, reopened, missing, recent);

        var result = await Handler().HandleAsync(Ct);

        result.Value.ShouldBe(new AutoCloseResult(4, 0, 0));
        _tickets.DidNotReceive().Update(Arg.Any<Ticket>());
        _staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_concurrency_conflict_on_one_ticket_does_not_stop_the_others()
    {
        var first = Solved();
        var second = Solved();
        Candidates(first, second);
        _clock.Advance(TimeSpan.FromDays(8));
        var unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict));

        var result = await Handler(unitOfWork).HandleAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AutoCloseResult(2, 1, 1));
    }

    [Fact]
    public async Task A_non_conflict_commit_failure_is_logged_and_skips_that_ticket_only()
    {
        var first = Solved();
        var second = Solved();
        Candidates(first, second);
        _clock.Advance(TimeSpan.FromDays(8));
        var logger = new CollectingLogger();
        var unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ReferenceViolation));
        var handler = new AutoCloseSolvedTicketsHandler(_tickets, unitOfWork, _clock, Options.Create(_options), logger);

        var result = await handler.HandleAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AutoCloseResult(2, 1, 0));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(PersistenceErrorCodes.ReferenceViolation));
    }

    [Fact]
    public async Task An_idle_run_logs_at_debug_and_a_busy_run_at_information()
    {
        var idle = new CollectingLogger();
        Candidates();
        await new AutoCloseSolvedTicketsHandler(_tickets, UnitOfWorkSubstitute.Create(), _clock, Options.Create(_options), idle).HandleAsync(Ct);
        idle.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);

        var busy = new CollectingLogger();
        Candidates(Solved());
        _clock.Advance(TimeSpan.FromDays(8));
        await new AutoCloseSolvedTicketsHandler(_tickets, UnitOfWorkSubstitute.Create(), _clock, Options.Create(_options), busy).HandleAsync(Ct);
        busy.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Information);
    }

    private sealed class CollectingLogger : ILogger<AutoCloseSolvedTicketsHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Running_twice_is_a_no_op_the_second_time()
    {
        var ticket = Solved();
        Candidates(ticket);
        _clock.Advance(TimeSpan.FromDays(8));
        var handler = Handler();

        (await handler.HandleAsync(Ct)).Value.Closed.ShouldBe(1);
        var second = await handler.HandleAsync(Ct);

        second.Value.ShouldBe(new AutoCloseResult(1, 0, 0));
        _staged.Count.ShouldBe(1);
        _tickets.Received(1).Update(Arg.Any<Ticket>());
    }
}
