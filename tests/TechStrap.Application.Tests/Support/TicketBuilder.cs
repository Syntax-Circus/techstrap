using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Support;

/// <summary>Builds tickets for handler tests through Domain calls, with pending changes accepted as if loaded from storage.</summary>
internal static class TicketBuilder
{
    public static Ticket New(TimeProvider clock, Guid? productId = null, Guid? requesterId = null, int sequence = 42)
    {
        var number = TicketNumber.Create("TS", sequence).Value;
        var ticket = Ticket.Create(
            number, productId ?? Guid.NewGuid(), requesterId ?? Guid.NewGuid(), "Cannot sign in", TicketChannel.Web, null, true, clock).Value;
        ticket.AcceptChanges();
        return ticket;
    }

    /// <summary>Walks New, Open, Pending, Solved, Closed through <c>ChangeStatus</c> as needed.</summary>
    public static Ticket InStatus(TicketStatus status, TimeProvider clock)
    {
        var ticket = New(clock);
        var actor = Actor.ForAgent(Guid.NewGuid());
        foreach (var step in PathTo(status))
        {
            ticket.ChangeStatus(step, actor, clock).IsSuccess.ShouldBeTrue();
        }

        ticket.AcceptChanges();
        return ticket;
    }

    /// <summary>A copy of the ticket restored with the given concurrency token.</summary>
    public static Ticket WithVersion(Ticket ticket, uint version) =>
        Ticket.Restore(
            ticket.Id, ticket.Number, ticket.ProductId, ticket.RequesterId, ticket.Subject, ticket.Status, ticket.Priority, ticket.AssigneeId,
            ticket.Channel, ticket.IsSpam, ticket.ParentTicketId, ticket.MetadataJson, ticket.MetadataTrusted, ticket.CustomFieldsJson,
            ticket.CreatedAt, ticket.FirstResponseAt, ticket.SolvedAt, ticket.ClosedAt, ticket.LastActivityAt, ticket.TagIds, version);

    private static TicketStatus[] PathTo(TicketStatus status) => status switch
    {
        TicketStatus.New => [],
        TicketStatus.Open => [TicketStatus.Open],
        TicketStatus.Pending => [TicketStatus.Open, TicketStatus.Pending],
        TicketStatus.Solved => [TicketStatus.Solved],
        TicketStatus.Closed => [TicketStatus.Solved, TicketStatus.Closed],
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
