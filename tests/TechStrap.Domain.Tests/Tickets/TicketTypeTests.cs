using TechStrap.Domain.Tickets;
namespace TechStrap.Domain.Tests.Tickets;
public sealed class TicketTypeTests
{
    [Fact] public void Existing_creation_defaults_to_support() => new TicketFactory().New().Type.ShouldBe(TicketType.Support);
    [Fact] public void Feature_request_creation_preserves_type_without_extra_change_event()
    {
        var f = new TicketFactory();
        var ticket = Ticket.Create(f.Number(), f.ProductId, f.RequesterId, "Feature", TicketChannel.Web, null, false, f.Clock, TicketType.FeatureRequest).Value;
        ticket.Type.ShouldBe(TicketType.FeatureRequest);
        ticket.PendingEvents.ShouldHaveSingleItem().Type.ShouldBe(TicketEventType.Created);
    }
    [Fact] public void Changing_type_is_audited_and_no_op_is_silent()
    {
        var f = new TicketFactory(); var ticket = f.Saved();
        ticket.ChangeType(TicketType.FeatureRequest, f.Agent, f.Clock).IsSuccess.ShouldBeTrue();
        ticket.PendingEvents.ShouldHaveSingleItem().Type.ShouldBe(TicketEventType.TypeChanged);
        ticket.ChangeType(TicketType.FeatureRequest, f.Agent, f.Clock).IsSuccess.ShouldBeTrue();
        ticket.PendingEvents.Count.ShouldBe(1);
        ticket.ChangeType((TicketType)999, f.Agent, f.Clock).Error!.Code.ShouldBe("type-invalid");
    }
    [Fact] public void Closed_feature_request_follow_up_inherits_type()
    {
        var f = new TicketFactory(); var ticket = f.Saved();
        ticket.ChangeType(TicketType.FeatureRequest, f.Agent, f.Clock);
        ticket.ChangeStatus(TicketStatus.Solved, f.Agent, f.Clock);
        ticket.ChangeStatus(TicketStatus.Closed, f.Agent, f.Clock);
        ticket.CreateFollowUp(f.Number(143), f.Clock).Value.Type.ShouldBe(TicketType.FeatureRequest);
        ticket.ChangeType(TicketType.Support, f.Agent, f.Clock).Error!.Code.ShouldBe("ticket-closed");
    }
}
