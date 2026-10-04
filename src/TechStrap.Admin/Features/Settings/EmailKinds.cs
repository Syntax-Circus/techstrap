namespace TechStrap.Admin.Features.Settings;

/// <summary>
/// The kinds of email the API queues (the <c>kind</c> of an outbox row), with a plain label for each. The audit log and the failed-emails page both name an email by its kind, so the words live in one place.
/// An unknown kind is shown as it came, never dropped.
/// </summary>
public static class EmailKinds
{
    public const string TicketConfirmation = "ticket-confirmation";
    public const string AgentReply = "agent-reply";
    public const string TicketSolved = "ticket-solved";
    public const string TicketAssigned = "ticket-assigned";
    public const string NewTicketAlert = "new-ticket-alert";
    public const string CustomerReplyAlert = "customer-reply-alert";
    public const string AccessLinks = "access-links";

    public static string Label(string? kind) => kind switch
    {
        TicketConfirmation => "Ticket confirmation",
        AgentReply => "Agent reply",
        TicketSolved => "Ticket solved",
        TicketAssigned => "Ticket assigned",
        NewTicketAlert => "New ticket alert",
        CustomerReplyAlert => "Customer reply alert",
        AccessLinks => "Access links",
        null or "" => "Email",
        _ => SafeText.Clip(kind),
    };
}
