namespace TechStrap.Application.Email;

/// <summary>Outbox kinds and their payloads (D-033): the payload is template data, never a rendered body.</summary>
public static class EmailTemplates
{
    public const string TicketConfirmation = "ticket-confirmation";
}

/// <summary>Payload of a ticket-confirmation outbox row. <see cref="AgentPublicName"/> is the already-resolved public name (D-024) or null.</summary>
public sealed record TicketConfirmationEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, string? AgentPublicName);
