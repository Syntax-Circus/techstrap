using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class ChangeTicketPriorityRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets;
    private List<(TicketEventType Type, string Payload)> _staged = [];

    public ChangeTicketPriorityRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(t => _staged = [.. t.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
    }

    private ChangeTicketPriorityRequestHandler Handler() => new(_claims, _agents, _tickets, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status = TicketStatus.Open, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    [Fact]
    public async Task A_new_priority_writes_PriorityChanged_and_returns_the_new_state()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketPriorityRequest("Urgent", 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RowVersion.ShouldBe(6u);
        ticket.Priority.ShouldBe(TicketPriority.Urgent);
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.PriorityChanged]);
        _tickets.Received(1).Update(ticket);
    }

    [Fact]
    public async Task The_same_priority_is_a_200_no_op()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketPriorityRequest(ticket.Priority.ToString(), 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        _staged.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("")]
    public async Task An_unknown_priority_is_a_field_error(string priority)
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketPriorityRequest(priority, 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("priority-invalid"), e => e.Target.ShouldBe("priority"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_closed_ticket_is_409()
    {
        var ticket = GivenTicket(TicketStatus.Closed);

        var result = await Handler().HandleAsync(ticket.Id, new ChangeTicketPriorityRequest("Urgent", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("ticket-closed");
    }
}
