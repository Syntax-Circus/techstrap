namespace TechStrap.Application.Email;

/// <summary>Outbox kinds and their payloads (D-033): the payload is template data, never a rendered body.</summary>
public static class EmailTemplates
{
    public const string TicketConfirmation = "ticket-confirmation";
    public const string AgentReply = "agent-reply";
    public const string TicketSolved = "ticket-solved";
    public const string TicketAssigned = "ticket-assigned";
}

/// <summary>Payload of a ticket-confirmation outbox row. <see cref="AgentPublicName"/> is the already-resolved public name (D-024) or null.</summary>
public sealed record TicketConfirmationEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, string? AgentPublicName);

/// <summary>Payload of an agent-reply row: no body (D-033, 16 000 cap); the Worker loads the message by id at send time.</summary>
public sealed record AgentReplyEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink,
    string AgentPublicName, Guid MessageId, bool Solved);

public sealed record TicketSolvedEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, int ReopenDays);

/// <summary>Internal alert to an agent; AdminLink is null when TECHSTRAP_ADMIN_PUBLIC_URL is not set.</summary>
public sealed record TicketAssignedEmail(string TicketNumber, string Subject, string ProductName, string? AssignedByName, string? AdminLink);
