using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Features;

public sealed class FeatureTicketLinkTests
{
    private readonly FakeTimeProvider _clock = new();
    private Ticket Ticket(
        TicketType type = TicketType.FeatureRequest) =>
        TechStrap.Domain.Tickets.Ticket.Create(TicketNumber.Create("TST", 1).Value, Guid.NewGuid(), Guid.NewGuid(), "Feature", TicketChannel.Web, null, false, _clock, type).Value;
    [Fact]

    public void A_linked_request_must_be_unlinked_before_moving_or_reclassification()
    {
        var t = Ticket();
        t.LinkFeature(Guid.NewGuid(), Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        t.MoveToProduct(Guid.NewGuid(), Actor.ForAgent(Guid.NewGuid()), _clock).Error!.Code.ShouldBe("feature-unlink-required");
        t.ChangeType(TicketType.Support, Actor.ForAgent(Guid.NewGuid()), _clock).Error!.Code.ShouldBe("feature-unlink-required");
        t.LinkFeature(null, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        t.ChangeType(TicketType.Support, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
    }
    [Fact]

    public void Support_tickets_cannot_link_to_features()
    {
        Ticket(TicketType.Support).LinkFeature(Guid.NewGuid(), Actor.ForAgent(Guid.NewGuid()), _clock).IsFailure.ShouldBeTrue();
    }
    [Fact]

    public void Linking_a_request_raises_an_audit_event()
    {
        var t = Ticket();
        var id = Guid.NewGuid();
        t.LinkFeature(id, Actor.ForAgent(Guid.NewGuid()), _clock);
        t.FeatureId.ShouldBe(id);
        t.PendingEvents.Last().Type.ShouldBe(TicketEventType.FeatureLinked);
    }
    [Fact]
    public void Restoring_a_linked_request_preserves_the_link_and_its_guards()
    {
        var ticket = Ticket();
        var featureId = Guid.NewGuid();
        var restored = TechStrap.Domain.Tickets.Ticket.Restore(
            ticket.Id, ticket.Number, ticket.ProductId, ticket.RequesterId, ticket.Subject,
            ticket.Status, ticket.Priority, ticket.AssigneeId, ticket.Channel, ticket.IsSpam,
            ticket.ParentTicketId, ticket.MetadataJson, ticket.MetadataTrusted, ticket.CustomFieldsJson,
            ticket.CreatedAt, ticket.FirstResponseAt, ticket.SolvedAt, ticket.ClosedAt,
            ticket.LastActivityAt, ticket.TagIds, 5, ticket.Type, featureId);
        restored.FeatureId.ShouldBe(featureId);
        restored.ChangeType(TicketType.Support, Actor.ForAgent(Guid.NewGuid()), _clock).IsFailure.ShouldBeTrue();
    }
}
