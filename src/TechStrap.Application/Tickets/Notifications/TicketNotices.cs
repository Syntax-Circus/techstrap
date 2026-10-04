namespace TechStrap.Application.Tickets.Notifications;

public static class TicketNotices
{
    /// <summary>Days a Solved ticket stays open to a reply before auto-close (D-008 default). Used when an old outbox row carries no window.</summary>
    public const int DefaultReopenDays = 7;
}
