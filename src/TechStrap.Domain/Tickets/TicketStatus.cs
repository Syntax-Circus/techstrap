namespace TechStrap.Domain.Tickets;

public enum TicketStatus
{
    New,
    Open,
    Pending,
    Solved,
    Closed,
}

/// <summary>The status transition table (02-ARCHITECTURE section 5). <c>is_spam</c> is orthogonal and not a status.</summary>
public static class TicketStatusRules
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Allowed = new()
    {
        [TicketStatus.New] = [TicketStatus.Open, TicketStatus.Pending, TicketStatus.Solved],
        [TicketStatus.Open] = [TicketStatus.Pending, TicketStatus.Solved],
        [TicketStatus.Pending] = [TicketStatus.Open, TicketStatus.Solved],
        [TicketStatus.Solved] = [TicketStatus.Open, TicketStatus.Closed],
        [TicketStatus.Closed] = [],
    };

    public static IReadOnlyList<TicketStatus> AllowedFrom(TicketStatus from) => Allowed[from];

    public static bool CanTransition(TicketStatus from, TicketStatus to) => Allowed[from].Contains(to);

    /// <summary>Closed is read-only: the only way to continue a closed conversation is a follow-up ticket.</summary>
    public static bool IsReadOnly(TicketStatus status) => status == TicketStatus.Closed;
}
