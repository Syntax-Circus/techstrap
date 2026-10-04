using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class AssignTicketRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();
    private readonly ITicketRepository _tickets;
    private readonly Agent _sam;
    private List<(TicketEventType Type, string Payload)> _staged = [];

    public AssignTicketRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(t => _staged = [.. t.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
    }

    private AssignTicketRequestHandler Handler() => new(_claims, _agents, _tickets, _planner, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status = TicketStatus.Open, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    private Agent GivenAgent(string subject, bool active = true)
    {
        var agent = Agent.Create(subject, subject, $"{subject}@example.com", AgentRole.Agent, _clock).Value;
        agent.SetActive(active);
        _agents.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    [Fact]
    public async Task Assigning_another_agent_writes_the_event_and_plans_their_alert()
    {
        var ticket = GivenTicket();
        var kim = GivenAgent("kim");

        var result = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(kim.Id, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssigneeId.ShouldBe(kim.Id);
        result.Value.RowVersion.ShouldBe(6u);
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.Assigned]);
        _tickets.Received(1).Update(ticket);
        await _planner.Received(1).PlanAssignedAsync(ticket, kim, _sam, Ct);
    }

    [Fact]
    public async Task Self_assignment_writes_the_event_and_the_planner_is_still_called_once()
    {
        var ticket = GivenTicket();
        _agents.GetByIdAsync(_sam.Id, Arg.Any<CancellationToken>()).Returns(_sam);

        var result = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(_sam.Id, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        ticket.AssigneeId.ShouldBe(_sam.Id);
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.Assigned]);
        await _planner.Received(1).PlanAssignedAsync(ticket, _sam, _sam, Ct);
    }

    [Fact]
    public async Task Reassigning_the_current_assignee_is_a_no_op_with_no_alert()
    {
        var ticket = GivenTicket();
        var kim = GivenAgent("kim");
        ticket.Assign(kim.Id, Actor.ForAgent(_sam.Id), _clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();

        var result = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(kim.Id, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        _staged.ShouldBeEmpty();
        await _planner.DidNotReceiveWithAnyArgs().PlanAssignedAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Unassigning_writes_the_event_and_plans_nothing()
    {
        var ticket = GivenTicket();
        var kim = GivenAgent("kim");
        ticket.Assign(kim.Id, Actor.ForAgent(_sam.Id), _clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();

        var result = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(null, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        ticket.AssigneeId.ShouldBeNull();
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.Assigned]);
        await _planner.DidNotReceiveWithAnyArgs().PlanAssignedAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task An_unknown_agent_is_404_and_an_inactive_one_is_400()
    {
        var ticket = GivenTicket();
        var gone = GivenAgent("gone", active: false);

        var unknown = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(Guid.NewGuid(), 5), Ct);
        var inactive = await Handler().HandleAsync(ticket.Id, new AssignTicketRequest(gone.Id, 5), Ct);

        unknown.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.NotFound), e => e.Code.ShouldBe("agent-not-found"));
        inactive.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("assignee-inactive"), e => e.Target.ShouldBe("assigneeId"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_closed_ticket_is_409_and_a_stale_row_version_is_409()
    {
        var closed = GivenTicket(TicketStatus.Closed);
        var open = GivenTicket();
        var kim = GivenAgent("kim");

        var onClosed = await Handler().HandleAsync(closed.Id, new AssignTicketRequest(kim.Id, 5), Ct);
        var stale = await Handler().HandleAsync(open.Id, new AssignTicketRequest(kim.Id, 4), Ct);

        onClosed.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict), e => e.Code.ShouldBe("ticket-closed"));
        stale.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict), e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
        await _planner.DidNotReceiveWithAnyArgs().PlanAssignedAsync(default!, default!, default!, Ct);
    }
}
