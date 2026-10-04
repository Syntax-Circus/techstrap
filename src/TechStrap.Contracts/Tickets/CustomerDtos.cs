namespace TechStrap.Contracts.Tickets;

/// <summary>
/// What a customer sees through their link (D-024, D-038). Public data only: no agent ids, emails or surnames, tags, internal
/// notes, events, LastActivityAt or other requesters. Status is a TicketStatuses name; the Portal maps it to words.
/// </summary>
public sealed record CustomerTicketDto(
    string Number, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages);

/// <summary>
/// One public message. AuthorType is a MessageAuthorTypes name; AuthorDisplayName is the resolved public agent name for agent
/// messages (AgentPublicIdentity) and null for the customer's own and system messages. BodyHtml is sanitised HTML.
/// </summary>
public sealed record CustomerMessageDto(
    Guid Id, string AuthorType, string? AuthorDisplayName, string BodyHtml, DateTimeOffset CreatedAt, IReadOnlyList<AttachmentDto> Attachments);
