using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class ActorValidationTests
{
    private readonly TicketFactory _factory = new();

    public static TheoryData<Actor> InvalidActors => new()
    {
        default(Actor),
        Actor.ForAgent(Guid.Empty),
        Actor.ForRequester(Guid.Empty),
        new Actor(ActorType.Agent, null),
        new Actor(ActorType.Requester, null),
        new Actor((ActorType)99, null),
    };

    [Theory]
    [MemberData(nameof(InvalidActors))]
    public void Every_public_mutation_rejects_an_agent_or_requester_actor_without_an_id(Actor actor)
    {
        var ticket = _factory.Saved();
        var clock = _factory.Clock;

        DomainResult[] results =
        [
            ticket.ChangeStatus(TicketStatus.Open, actor, clock),
            ticket.Assign(Guid.NewGuid(), actor, clock),
            ticket.ChangePriority(TicketPriority.High, actor, clock),
            ticket.MoveToProduct(Guid.NewGuid(), actor, clock),
            ticket.AddTag(Guid.NewGuid(), actor, clock),
            ticket.RemoveTag(Guid.NewGuid(), actor, clock),
            ticket.MarkSpam(true, actor, clock),
        ];

        results.ShouldAllBe(result => result.IsFailure && result.Error!.Code == "actor-invalid" && result.Error.Kind == DomainErrorKind.Validation);
        ticket.PendingEvents.ShouldBeEmpty();
        ticket.Status.ShouldBe(TicketStatus.New);
    }

    [Fact]
    public void A_system_actor_and_an_actor_with_an_id_are_accepted()
    {
        var ticket = _factory.Saved();

        ticket.ChangeStatus(TicketStatus.Open, Actor.System, _factory.Clock).IsSuccess.ShouldBeTrue();
        ticket.ChangePriority(TicketPriority.High, _factory.Agent, _factory.Clock).IsSuccess.ShouldBeTrue();
        ticket.MarkSpam(true, Actor.ForRequester(_factory.RequesterId), _factory.Clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_reply_with_an_empty_author_id_is_rejected()
    {
        var ticket = _factory.Saved();

        ticket.AddAgentReply(Guid.Empty, "x", _factory.Clock).Error!.Code.ShouldBe("author-required");
        ticket.AddInternalNote(Guid.Empty, "x", _factory.Clock).Error!.Code.ShouldBe("author-required");
        ticket.AddCustomerReply(Guid.Empty, "x", _factory.Clock).Error!.Code.ShouldBe("author-required");
        ticket.PendingEvents.ShouldBeEmpty();
        ticket.PendingMessages.ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_cannot_be_created_for_an_empty_requester_id()
    {
        var result = Ticket.Create(_factory.Number(), _factory.ProductId, Guid.Empty, "Subject", TicketChannel.Web, null, false, _factory.Clock);

        result.Error!.Code.ShouldBe("requester-id-required");
    }
}
