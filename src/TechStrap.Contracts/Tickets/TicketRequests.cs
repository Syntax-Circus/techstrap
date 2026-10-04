namespace TechStrap.Contracts.Tickets;

/// <summary>List tickets with optional filtering and pagination.</summary>
public sealed record ListTicketsRequest(
    string? View, Guid? ProductId, string? Status, string? Priority, Guid? AssigneeId,
    Guid? TagId, Guid? RequesterId, string? Search, int Page, int PageSize);

/// <summary>StatusAfter: null or "Pending" (default Domain behaviour) or "Solved" ("Send and solve"). Files travel beside this record (D-016).</summary>
public sealed record AddAgentReplyRequest(string? Body, IReadOnlyList<Guid>? LinkedArticleIds, string? StatusAfter, uint? RowVersion);

/// <summary>Add an internal note to a ticket.</summary>
public sealed record AddInternalNoteRequest(string? Body, uint? RowVersion);

/// <summary>Change a ticket's status.</summary>
public sealed record ChangeTicketStatusRequest(string? Status, uint? RowVersion);

/// <summary>Assign or unassign a ticket.</summary>
public sealed record AssignTicketRequest(Guid? AssigneeId, uint? RowVersion);

/// <summary>Change a ticket's priority.</summary>
public sealed record ChangeTicketPriorityRequest(string? Priority, uint? RowVersion);

/// <summary>Move a ticket to a different product.</summary>
public sealed record MoveTicketProductRequest(Guid? ProductId, uint? RowVersion);

/// <summary>Add a tag to a ticket.</summary>
public sealed record AddTicketTagRequest(Guid? TagId, uint? RowVersion);

/// <summary>Mark or unmark a ticket as spam.</summary>
public sealed record MarkTicketSpamRequest(bool? IsSpam, uint? RowVersion);
