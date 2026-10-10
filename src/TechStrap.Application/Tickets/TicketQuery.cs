using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

/// <summary>
/// The agent queue views (FR-TKT-01, D-024). All but <see cref="Spam"/> exclude spam. "Active" below means New, Open or Pending.
/// Assumption (confirm in PHASE-06): Open shows New and Open tickets, since a New ticket still needs an agent.
/// </summary>
public enum TicketView
{
    /// <summary>Active, not spam, no assignee.</summary>
    Unassigned,

    /// <summary>Active, not spam, assigned to <see cref="TicketQuery.AgentId"/>.</summary>
    Mine,

    /// <summary>Status New or Open, not spam.</summary>
    Open,

    /// <summary>Status Pending, not spam.</summary>
    Pending,

    /// <summary>Every status, not spam.</summary>
    All,

    /// <summary>Every status, spam only.</summary>
    Spam,
}

/// <summary>
/// Filters for a ticket list. Optional filters narrow the view further. Sorted by last activity, newest first, unless
/// <see cref="SearchText"/> is set: then tickets are full-text matched (subject, message bodies, exact ticket number) and sorted by
/// relevance, a subject match above a body match, ties by last activity (D-011, D-027). The text uses web-search syntax
/// (quotes, <c>or</c>, <c>-exclude</c>); blank text means no search. Implementations normalize <see cref="Page"/> and
/// <see cref="PageSize"/> through <see cref="Paging"/> before querying. Agent-only: the search text matches internal-note bodies as well
/// as public ones, so a handler must never run a query with <see cref="SearchText"/> on behalf of a customer (D-024); equal sort keys
/// are tied by ticket id so pages are stable.
/// </summary>
public sealed record TicketQuery(
    TicketView View,
    Guid? AgentId = null,
    Guid? ProductId = null,
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    Guid? AssigneeId = null,
    Guid? TagId = null,
    Guid? RequesterId = null,
    string? SearchText = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// One row of a ticket list. Agent-only: it carries the requester email and <see cref="LastActivityAt"/> (which includes internal
/// activity), so it must never be returned to customers.
/// </summary>
public sealed record TicketSummary(
    Guid Id,
    string Number,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    Guid ProductId,
    Guid RequesterId,
    string RequesterEmail,
    string? RequesterName,
    Guid? AssigneeId,
    bool IsSpam,
    IReadOnlyList<Guid> TagIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);

/// <summary>Ticket counts for the queue tabs; each count has exactly the membership of the matching <see cref="TicketView"/> list.</summary>
public sealed record TicketViewCounts(int Unassigned, int Mine, int Open, int Pending, int All, int Spam);

/// <summary>A fresh, untracked read of the ticket's mutable state and concurrency token, taken after a commit.</summary>
public sealed record TicketState(
    Guid Id,
    string Number,
    TicketStatus Status,
    TicketPriority Priority,
    Guid ProductId,
    Guid? AssigneeId,
    bool IsSpam,
    IReadOnlyList<Guid> TagIds,
    DateTimeOffset LastActivityAt,
    uint Version);

/// <summary>A follow-up created from a parent, with its first public message body (sanitized HTML) and that message's attachment file names for the dedupe check.</summary>
public sealed record FollowUpCandidate(
    Guid TicketId, string Number, Guid FirstMessageId, string FirstMessageBody, DateTimeOffset CreatedAt, IReadOnlyList<string> FirstMessageFileNames);

/// <summary>A requester's ticket for the lost-link email.</summary>
public sealed record RequesterTicketLink(Guid TicketId, Guid ProductId, string Number, string Subject, DateTimeOffset LastActivityAt);
