using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>The rules every live component shares, in one place so the queue, the detail page and the presence bar cannot disagree (D-046).</summary>
public static class LiveChangeRules
{
    public static bool IsResync(TicketChangedDto change) => change.Kind == TicketChangeKinds.Resync;

    /// <summary>
    /// True when the signed-in agent made the change: their own write already refreshed their page, and another tab of theirs still meets the existing 409 on a send. A <c>Resync</c> is never own.
    /// A change with no actor (the Worker's auto-close, a customer reply) is never own.
    /// </summary>
    public static bool IsOwn(TicketChangedDto change, Guid? ownAgentId) =>
        !IsResync(change) && ownAgentId is { } own && change.ActorAgentId == own;

    /// <summary>
    /// Every <c>TicketChanged</c> goes to every connection (D-046), so a page filters by ticket id. A <c>Resync</c> names no ticket and concerns every page.
    /// </summary>
    public static bool Concerns(TicketChangedDto change, Guid ticketId) => IsResync(change) || change.TicketId == ticketId;

    /// <summary>The message a page gets after a reconnect: the same shape the Api's listener sends after it reconnects to Postgres, built here because the notifications of the gap are gone.</summary>
    public static TicketChangedDto NewResync(DateTimeOffset now) =>
        new(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, now, TicketChangeKinds.Resync);
}
