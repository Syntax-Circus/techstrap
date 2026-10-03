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
/// (quotes, <c>or</c>, <c>-exclude</c>); blank text means no search.
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

/// <summary>One row of a ticket list.</summary>
public sealed record TicketSummary(
    Guid Id,
    string Number,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    Guid ProductId,
    Guid RequesterId,
    string RequesterEmail,
    Guid? AssigneeId,
    bool IsSpam,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);
