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

public sealed class MarkTicketSpamRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets;
    private List<(TicketEventType Type, string Payload)> _staged = [];

    public MarkTicketSpamRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(t => _staged = [.. t.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
    }

    private MarkTicketSpamRequestHandler Handler() => new(_claims, _agents, _tickets, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status = TicketStatus.Open, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [.. ticket.TagIds], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    [Fact]
    public async Task Marking_spam_sets_the_flag_writes_MarkedSpam_and_plans_nothing()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new MarkTicketSpamRequest(true, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSpam.ShouldBeTrue();
        ticket.IsSpam.ShouldBeTrue();
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.MarkedSpam]);
        _tickets.Received(1).Update(ticket);
    }

    [Fact]
    public async Task Not_spam_clears_the_flag_and_keeps_the_status()
    {
        var ticket = GivenTicket(TicketStatus.Pending);
        ticket.MarkSpam(true, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();

        var result = await Handler().HandleAsync(ticket.Id, new MarkTicketSpamRequest(false, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSpam.ShouldBeFalse();
        result.Value.Status.ShouldBe("Pending");
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.MarkedSpam]);
    }

    [Fact]
    public async Task Repeating_the_current_value_is_a_no_op()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new MarkTicketSpamRequest(false, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        _staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_flag_is_400()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, new MarkTicketSpamRequest(null, 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("is-spam-required"), e => e.Target.ShouldBe("isSpam"));
    }

    [Fact]
    public async Task A_closed_ticket_is_409_ticket_closed()
    {
        var ticket = GivenTicket(TicketStatus.Closed);

        var result = await Handler().HandleAsync(ticket.Id, new MarkTicketSpamRequest(true, 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict), e => e.Code.ShouldBe("ticket-closed"));
    }
}
