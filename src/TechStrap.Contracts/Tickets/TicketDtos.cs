namespace TechStrap.Contracts.Tickets;

/// <summary>A ticket tag in a list or detail view.</summary>
public sealed record TicketTagDto(Guid Id, string Name, string Colour);

/// <summary>One row of a ticket list.</summary>
public sealed record TicketSummaryDto(
    Guid Id, string Number, string Subject, string Status, string Priority,
    Guid ProductId, string ProductName,
    Guid RequesterId, string RequesterEmail, string? RequesterName,
    Guid? AssigneeId, string? AssigneeName,
    bool IsSpam, IReadOnlyList<TicketTagDto> Tags,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt);

/// <summary>Counts of tickets by view.</summary>
public sealed record TicketViewCountsResponse(int Unassigned, int Mine, int Open, int Pending, int All, int Spam);

/// <summary>A file attached to a message.</summary>
public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size);

/// <summary>A knowledge article linked from a message.</summary>
public sealed record LinkedArticleDto(Guid Id, string Title, string Slug);

/// <summary>A timeline message. BodyHtml is sanitized HTML. AuthorName is the agent's own name (agent views only) or the requester's name or email.</summary>
public sealed record MessageDto(
    Guid Id, string AuthorType, Guid? AuthorId, string? AuthorName, string Visibility, string BodyHtml,
    DateTimeOffset CreatedAt, IReadOnlyList<AttachmentDto> Attachments, IReadOnlyList<LinkedArticleDto> LinkedArticles);

/// <summary>A ticket event. PayloadJson holds ids and enum names only (never free text).</summary>
public sealed record TicketEventDto(
    Guid Id, string Type, string ActorType, Guid? ActorId, string? ActorName, string PayloadJson, DateTimeOffset OccurredAt);

/// <summary>The ticket requester.</summary>
public sealed record TicketRequesterDto(Guid Id, string Email, string? Name, string? ExternalUserRef);

/// <summary>A ticket with its full timeline, messages and events.</summary>
public sealed record TicketDetailDto(
    Guid Id, string Number, string Subject, string Status, string Priority,
    Guid ProductId, string ProductName, TicketRequesterDto Requester,
    Guid? AssigneeId, string? AssigneeName, bool IsSpam, IReadOnlyList<TicketTagDto> Tags,
    string Channel, Guid? ParentTicketId, string? MetadataJson, bool MetadataTrusted,
    DateTimeOffset CreatedAt, DateTimeOffset? FirstResponseAt, DateTimeOffset? SolvedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset LastActivityAt, uint RowVersion,
    IReadOnlyList<MessageDto> Messages, IReadOnlyList<TicketEventDto> Events);

/// <summary>Returned by every ticket write (D-036): the fresh state and the RowVersion for the next write.</summary>
public sealed record TicketStateDto(
    Guid Id, string Number, string Status, string Priority, Guid ProductId, Guid? AssigneeId,
    bool IsSpam, IReadOnlyList<Guid> TagIds, DateTimeOffset LastActivityAt, uint RowVersion);

/// <summary>The response to a ticket message operation, containing the new message and the updated ticket state.</summary>
public sealed record AgentMessageResponse(MessageDto Message, TicketStateDto Ticket);
