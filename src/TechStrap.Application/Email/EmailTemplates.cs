namespace TechStrap.Application.Email;

/// <summary>Outbox kinds and their payloads (D-033): the payload is template data, never a rendered body.</summary>
public static class EmailTemplates
{
    public const string TicketConfirmation = "ticket-confirmation";
    public const string AgentReply = "agent-reply";
    public const string TicketSolved = "ticket-solved";
    public const string TicketAssigned = "ticket-assigned";
    public const string NewTicketAlert = "new-ticket-alert";
    public const string CustomerReplyAlert = "customer-reply-alert";
    public const string AccessLinks = "access-links";
}

/// <summary>Payload of a ticket-confirmation outbox row. <see cref="AgentPublicName"/> is the already-resolved public name (D-024) or null.</summary>
public sealed record TicketConfirmationEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, string? AgentPublicName);

/// <summary>Payload of an agent-reply row: no body (D-033, 16 000 cap); the Worker loads the message by id at send time. ReopenDays 0 means a row queued before 06b: the renderer uses TicketNotices.DefaultReopenDays.</summary>
public sealed record AgentReplyEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink,
    string AgentPublicName, Guid MessageId, bool Solved, int ReopenDays = 0, IReadOnlyList<ArticleLinkEntry>? Articles = null);

/// <summary>A KB article the reply links, as it appears in the customer email: its title and the absolute portal URL (D-044). A row queued before PHASE-08 has none. <c>ArticleId</c> lets the Worker re-check at send time that the article is still Published at that address; a row without it is rendered as it is.</summary>
public sealed record ArticleLinkEntry(string Title, string Url, Guid? ArticleId = null);

public sealed record TicketSolvedEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, int ReopenDays);

/// <summary>Internal alert to an agent; AdminLink is null when TECHSTRAP_ADMIN_PUBLIC_URL is not set.</summary>
public sealed record TicketAssignedEmail(string TicketNumber, string Subject, string ProductName, string? AssignedByName, string? AdminLink);

/// <summary>Internal alert to an opted-in agent. RequesterLabel is the requester's name or email (agents may see it).</summary>
public sealed record NewTicketAlertEmail(string TicketNumber, string Subject, string ProductName, string RequesterLabel, bool IsFollowUp, string? AdminLink);

/// <summary>Internal alert: a customer replied. Reopened is true when the reply moved Pending/Solved to Open.</summary>
public sealed record CustomerReplyAlertEmail(string TicketNumber, string Subject, string ProductName, bool Reopened, string? AdminLink);

/// <summary>Lost-link email to the requester's own address (D-038): at most LostLinkOptions.MaxLinks entries.</summary>
public sealed record AccessLinksEmail(string? RequesterName, IReadOnlyList<AccessLinkEntry> Links);

public sealed record AccessLinkEntry(string TicketNumber, string Subject, string PortalLink);
