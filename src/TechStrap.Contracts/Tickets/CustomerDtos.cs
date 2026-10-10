namespace TechStrap.Contracts.Tickets;

/// <summary>
/// What a customer sees through their link (D-024, D-038). Public data only: no agent ids, emails or surnames, tags, internal
/// notes, events, LastActivityAt or other requesters. Status is a TicketStatuses name; the Portal maps it to words.
/// ProductKey is the public key of the ticket's product (never its internal id): the link has no product in it, so the Portal asks the
/// public product endpoint for this key to theme the page (D-045 addendum, 2026-10-06).
/// </summary>
public sealed record CustomerTicketDto(
    string Number, string ProductKey, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages);

/// <summary>
/// One public message. AuthorType is a MessageAuthorTypes name; AuthorDisplayName is the resolved public agent name for agent
/// messages (AgentPublicIdentity) and null for the customer's own and system messages. BodyHtml is sanitized HTML.
/// </summary>
public sealed record CustomerMessageDto(
    Guid Id, string AuthorType, string? AuthorDisplayName, string BodyHtml, DateTimeOffset CreatedAt, IReadOnlyList<AttachmentDto> Attachments);
