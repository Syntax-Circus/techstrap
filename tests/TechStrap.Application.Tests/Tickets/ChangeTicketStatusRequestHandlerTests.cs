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

public sealed class ChangeTicketStatusRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = TicketRepositorySubstitute.Create();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();

    public ChangeTicketStatusRequestHandlerTests()
    {
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
    }

    public static TheoryData<TicketStatus, TicketStatus> AllowedTransitions() => Pairs(allowed: true);

    public static TheoryData<TicketStatus, TicketStatus> ForbiddenTransitions() => Pairs(allowed: false);

    private static TheoryData<TicketStatus, TicketStatus> Pairs(bool allowed)
    {
        var data = new TheoryData<TicketStatus, TicketStatus>();
        foreach (var from in Enum.GetValues<TicketStatus>())
        {
            foreach (var to in Enum.GetValues<TicketStatus>())
            {
                if (TicketStatusRules.AllowedFrom(from).Contains(to) == allowed)
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    private ChangeTicketStatusRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_claims, _agents, _tickets, _planner, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public async Task Every_allowed_transition_succeeds(TicketStatus from, TicketStatus to)
    {
        var ticket = GivenTicket(from);

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest(to.ToString(), 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(to.ToString());
        result.Value.RowVersion.ShouldBe(6u);
        ticket.Status.ShouldBe(to);
        _tickets.Received(1).Update(ticket);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public async Task Every_forbidden_transition_is_409_and_nothing_is_committed(TicketStatus from, TicketStatus to)
    {
        var ticket = GivenTicket(from);
        var scope = Substitute.For<IUnitOfWorkScope>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(scope));

        var result = await Handler(unitOfWork).HandleAsync(ticket.Id, new ChangeTicketStatusRequest(to.ToString(), 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(from == TicketStatus.Closed ? "ticket-closed" : "invalid-status-transition"));
        ticket.Status.ShouldBe(from);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
        await scope.DidNotReceiveWithAnyArgs().CommitAsync(Ct);
        await _planner.DidNotReceiveWithAnyArgs().PlanSolvedAsync(default!, Ct);
    }

    [Fact]
    public async Task Solving_plans_the_solved_notice_and_other_changes_do_not()
    {
        var open = GivenTicket(TicketStatus.Open);
        (await Handler().HandleAsync(open.Id, new ChangeTicketStatusRequest("Pending", 5), Ct)).IsSuccess.ShouldBeTrue();
        await _planner.DidNotReceiveWithAnyArgs().PlanSolvedAsync(default!, Ct);

        var other = GivenTicket(TicketStatus.Open);
        (await Handler().HandleAsync(other.Id, new ChangeTicketStatusRequest("Solved", 5), Ct)).IsSuccess.ShouldBeTrue();
        await _planner.Received(1).PlanSolvedAsync(other, Ct);
    }

    [Fact]
    public async Task Solved_at_is_set_on_solve_and_cleared_on_reopen()
    {
        var ticket = GivenTicket(TicketStatus.Open);

        (await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest("Solved", 5), Ct)).IsSuccess.ShouldBeTrue();
        ticket.SolvedAt.ShouldNotBeNull();

        (await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest("Open", 5), Ct)).IsSuccess.ShouldBeTrue();
        ticket.SolvedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_missing_row_version_is_400_and_a_stale_one_is_409()
    {
        var ticket = GivenTicket(TicketStatus.Open);

        var missing = await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest("Pending", null), Ct);
        var stale = await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest("Pending", 4), Ct);

        missing.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("row-version-required"));
        stale.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict), e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        ticket.Status.ShouldBe(TicketStatus.Open);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("")]
    public async Task An_unknown_status_is_a_field_error(string status)
    {
        var ticket = GivenTicket(TicketStatus.Open);

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketStatusRequest(status, 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("status-invalid"),
            e => e.Target.ShouldBe("status"));
    }
}
