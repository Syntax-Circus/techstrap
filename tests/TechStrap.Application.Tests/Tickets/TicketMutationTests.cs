using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class TicketMutationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();

    private Agent GivenActiveAgent(bool active = true)
    {
        var agent = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        agent.SetActive(active);
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    private Ticket GivenTicket(uint version)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.New(_clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        return ticket;
    }

    private Task<Result<(Agent Agent, Ticket Ticket)>> Load(Guid id, uint? rowVersion, bool required) =>
        TicketMutation.LoadAsync(id, rowVersion, required, _claims, _agents, _tickets, Ct);

    [Fact]
    public async Task A_required_row_version_that_is_missing_is_a_field_error_before_any_lookup()
    {
        var result = await Load(Guid.NewGuid(), null, true);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("row-version-required"),
            e => e.Target.ShouldBe("rowVersion"));
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_ticket_is_not_found()
    {
        GivenActiveAgent();

        var result = await Load(Guid.NewGuid(), null, false);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.NotFound),
            e => e.Code.ShouldBe("ticket-not-found"));
    }

    [Fact]
    public async Task A_stale_row_version_is_a_concurrency_conflict()
    {
        GivenActiveAgent();
        var ticket = GivenTicket(7);

        var result = await Load(ticket.Id, 6, true);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
    }

    [Fact]
    public async Task A_matching_or_omitted_optional_row_version_loads_the_ticket()
    {
        var agent = GivenActiveAgent();
        var ticket = GivenTicket(7);

        var matching = await Load(ticket.Id, 7, false);
        var omitted = await Load(ticket.Id, null, false);

        matching.Value.Ticket.ShouldBeSameAs(ticket);
        matching.Value.Agent.ShouldBeSameAs(agent);
        omitted.Value.Ticket.ShouldBeSameAs(ticket);
    }

    [Fact]
    public async Task An_unprovisioned_or_inactive_agent_is_refused()
    {
        var ticket = GivenTicket(1);

        var noClaims = await Load(ticket.Id, null, false);
        noClaims.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.AccessRequired);

        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));
        var unprovisioned = await Load(ticket.Id, null, false);
        unprovisioned.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.NotProvisioned);

        GivenActiveAgent(active: false);
        var inactive = await Load(ticket.Id, null, false);
        inactive.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.Inactive);
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_and_state_read_are_separate_steps_and_the_read_ignores_cancellation()
    {
        var id = Guid.NewGuid();
        _tickets.GetStateAsync(id, Arg.Any<CancellationToken>()).Returns(
            new TicketState(id, "TS-42", TicketStatus.Open, TicketPriority.Normal, Guid.NewGuid(), null, false, [], _clock.GetUtcNow(), 3));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using (var scope = await UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")).BeginAsync(Ct))
        {
            var committed = await TicketMutation.CommitAsync(scope, Ct);

            committed.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        }

        var state = await TicketMutation.ReadStateAsync(id, _tickets, cancelled.Token);

        state.Value.RowVersion.ShouldBe(3u);
        await _tickets.Received(1).GetStateAsync(id, CancellationToken.None);
        (await TicketMutation.ReadStateAsync(Guid.NewGuid(), _tickets, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("ticket-not-found");
    }

    [Fact]
    public async Task Commit_returns_the_fresh_state_and_passes_commit_conflicts_through()
    {
        var id = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var now = _clock.GetUtcNow();
        _tickets.GetStateAsync(id, Arg.Any<CancellationToken>()).Returns(
            new TicketState(id, "TS-42", TicketStatus.Pending, TicketPriority.High, Guid.NewGuid(), null, false, [tagId], now, 9));

        await using (var scope = await UnitOfWorkSubstitute.Create().BeginAsync(Ct))
        {
            var ok = await TicketMutation.CommitAsync(scope, id, _tickets, Ct);

            ok.Value.ShouldSatisfyAllConditions(
                s => s.Status.ShouldBe("Pending"),
                s => s.Priority.ShouldBe("High"),
                s => s.RowVersion.ShouldBe(9u),
                s => s.TagIds.ShouldBe([tagId]),
                s => s.Number.ShouldBe("TS-42"));
        }

        await using (var scope = await UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")).BeginAsync(Ct))
        {
            var conflict = await TicketMutation.CommitAsync(scope, id, _tickets, Ct);

            conflict.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
                e => e.Code.ShouldBe("concurrency-conflict"));
        }

        await using (var scope = await UnitOfWorkSubstitute.Create().BeginAsync(Ct))
        {
            var missing = await TicketMutation.CommitAsync(scope, Guid.NewGuid(), _tickets, Ct);

            missing.Errors.ShouldHaveSingleItem().Code.ShouldBe("ticket-not-found");
        }
    }
}
