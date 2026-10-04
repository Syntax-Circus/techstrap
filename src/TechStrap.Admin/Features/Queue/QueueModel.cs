using Microsoft.AspNetCore.WebUtilities;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Queue;

/// <summary>The queue's named constants (PHASE-07 boundary validation: page size and debounce are never literals at a call site).</summary>
public static class QueueDefaults
{
    /// <summary>Tickets per page. The API's own default is 25 as well; the queue states it so a server change cannot move it.</summary>
    public const int PageSize = 25;

    /// <summary>How long the search box waits after the last keystroke before it asks the API.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The view shown at <c>/</c> and at <c>/queue</c> (UX-BRIEF-admin). Not the API's <c>TicketViews.Default</c>, which is All.</summary>
    public const string View = TicketViews.Unassigned;
}

/// <summary>The query-string keys of the queue. Every filter lives in the URL so views are linkable and survive a refresh.</summary>
public static class QueueQueryKeys
{
    public const string Product = "product";
    public const string Status = "status";
    public const string Priority = "priority";
    public const string Tag = "tag";
    public const string Search = "search";
    public const string Page = "page";
}

/// <summary>Everything that decides which tickets the queue shows. Record equality is how the page knows a parameter change needs a reload.</summary>
public sealed record QueueFilter(string View, Guid? ProductId, string? Status, string? Priority, Guid? TagId, string? Search, int Page)
{
    public bool HasFilters => ProductId is not null || Status is not null || Priority is not null || TagId is not null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>The API request. Assignee and requester filters are not part of the queue UI (requester history is a later phase).</summary>
    public ListTicketsRequest ToRequest() =>
        new(View, ProductId, Status, Priority, AssigneeId: null, TagId, RequesterId: null, Search, Page, QueueDefaults.PageSize);

    /// <summary>The same view with no filters, no search and the first page.</summary>
    public QueueFilter Cleared() => new(View, null, null, null, null, null, 1);
}

public static class QueueViews
{
    /// <summary>The six views, in rail order. Spam is separate by design: the five before it never include it (D-024).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TicketViews.Unassigned, TicketViews.Mine, TicketViews.Open, TicketViews.Pending, TicketViews.All, TicketViews.Spam,
    ];

    /// <summary>The canonical view for a route segment (case-insensitive); the default view for none; null for an unknown segment.</summary>
    public static string? Parse(string? segment) =>
        string.IsNullOrEmpty(segment)
            ? QueueDefaults.View
            : All.FirstOrDefault(view => view.Equals(segment, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string view) => view.ToLowerInvariant();

    public static int CountOf(TicketViewCountsResponse counts, string view) => view switch
    {
        TicketViews.Unassigned => counts.Unassigned,
        TicketViews.Mine => counts.Mine,
        TicketViews.Open => counts.Open,
        TicketViews.Pending => counts.Pending,
        TicketViews.All => counts.All,
        TicketViews.Spam => counts.Spam,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unknown queue view."),
    };
}

public static class QueueLinks
{
    /// <summary>The URL of a filter: <c>/queue/mine?product=...&amp;search=...&amp;page=2</c>. Page 1 and empty values are left out.</summary>
    public static string Uri(QueueFilter filter)
    {
        var query = new Dictionary<string, string?>
        {
            [QueueQueryKeys.Product] = filter.ProductId?.ToString(),
            [QueueQueryKeys.Status] = filter.Status,
            [QueueQueryKeys.Priority] = filter.Priority,
            [QueueQueryKeys.Tag] = filter.TagId?.ToString(),
            [QueueQueryKeys.Search] = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search,
            [QueueQueryKeys.Page] = filter.Page > 1 ? filter.Page.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return QueryHelpers.AddQueryString($"/queue/{QueueViews.Slug(filter.View)}", query.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary());
    }
}

/// <summary>One queue row, ready to draw. Mapping is simple, so there is no factory (PHASE-07 component boundaries).</summary>
public sealed record TicketRowViewModel(
    Guid Id,
    string Number,
    string Href,
    string Subject,
    IReadOnlyList<TicketTagDto> Tags,
    string ProductName,
    string Requester,
    StampStatus Stamp,
    PriorityLevel Priority,
    string? AssigneeName,
    string AssigneeInitials,
    DateTimeOffset LastActivity)
{
    public static TicketRowViewModel From(TicketSummaryDto ticket) => new(
        ticket.Id,
        ticket.Number,
        $"/tickets/{Uri.EscapeDataString(ticket.Number)}",
        ticket.Subject,
        ticket.Tags,
        ticket.ProductName,
        string.IsNullOrWhiteSpace(ticket.RequesterName) ? ticket.RequesterEmail : ticket.RequesterName,
        TicketDisplay.Stamp(ticket.Status, ticket.IsSpam),
        TicketDisplay.Priority(ticket.Priority),
        ticket.AssigneeName,
        TicketDisplay.Initials(ticket.AssigneeName),
        ticket.LastActivityAt);
}

/// <summary>The queue's copy. Plain text; the one brand moment (All caught up) takes its words from <see cref="CaughtUpCopy"/>.</summary>
public static class QueueCopy
{
    public const string Heading = "Queue";
    public const string LoadFailed = "Couldn't load tickets.";
    public const string Refresh = "Refresh";
    public const string ClearFilters = "Clear filters";
    public const string NoMatchHeading = "No tickets match";
    public const string NoPendingHeading = "No pending tickets";
    public const string NoTicketsHeading = "No tickets yet";
    public const string NoSpamHeading = "No spam";
    public const string SearchLabel = "Search tickets";
    public const string AllProducts = "All products";
    public const string AllStatuses = "Any status";
    public const string AllPriorities = "Any priority";
    public const string AllTags = "Any tag";
}

/// <summary>The "All caught up" brand moment (UX-BRIEF-admin, Brand moments): a truly empty Unassigned, Mine or Open view with no filters.</summary>
public static class CaughtUpCopy
{
    public const string Title = "queue.exe — 0 items";
    public const string Heading = "All caught up";
    public const string Body = "Zero tickets, fully supported.";
    public const string OpenTicketsLink = "View open tickets";
    public const string AllTicketsLink = "View all tickets";
}
