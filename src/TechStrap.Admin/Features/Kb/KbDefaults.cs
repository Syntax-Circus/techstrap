using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>The knowledge base screens' named constants: page sizes and debounce times are never literals at a call site (PHASE-08 boundary validation).</summary>
public static class KbDefaults
{
    /// <summary>Articles per page in the list.</summary>
    public const int PageSize = 25;

    /// <summary>Results the reply composer's article picker shows.</summary>
    public const int PickerPageSize = 10;

    /// <summary>How long the list's search box waits after the last keystroke before it asks the API.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the editor waits after the last keystroke before it asks the API for a new preview (PHASE-08).</summary>
    public static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the article picker waits after the last keystroke before it searches.</summary>
    public static readonly TimeSpan PickerDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The picture types the editor offers, by extension. The API decides by the file's own bytes; this only keeps an obviously wrong pick from being sent.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>The statuses the list filter offers; a status in the URL that is not one of these is dropped.</summary>
    public static IReadOnlyList<string> Statuses { get; } = [KbArticleStatuses.Draft, KbArticleStatuses.Published, KbArticleStatuses.Archived];

    /// <summary>The canonical spelling of <paramref name="value"/> from <see cref="Statuses"/> (case-insensitive), or null when it is blank or unknown.</summary>
    public static string? CanonicalStatus(string? value) => Statuses.FirstOrDefault(status => status.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The query-string keys of the article list. Every filter lives in the URL so a view is linkable and survives a refresh.</summary>
public static class KbQueryKeys
{
    public const string Product = "product";
    public const string Category = "category";
    public const string Status = "status";
    public const string Search = "search";
    public const string Page = "page";
}

/// <summary>
/// Everything that decides which articles the list shows. Record equality is how the page knows a parameter change needs a reload. A product filter shows that product's own articles; the
/// shared ones have their own choice (<see cref="SharedOnly"/>, the word "shared" in the URL), so the two never mix on one screen.
/// </summary>
public sealed record KbListFilter(Guid? ProductId, bool SharedOnly, Guid? CategoryId, string? Status, string? Search, int Page)
{
    /// <summary>The value of the product query key (and of the select) that means the shared articles.</summary>
    public const string SharedValue = "shared";

    public static KbListFilter Empty { get; } = new(null, false, null, null, null, 1);

    public bool HasFilters => ProductId is not null || SharedOnly || CategoryId is not null || Status is not null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>The same list with no filters, no search and the first page.</summary>
    public KbListFilter Cleared() => Empty;

    /// <summary>What the product select shows: the product's id, "shared", or nothing for every product.</summary>
    public string ProductValue => SharedOnly ? SharedValue : ProductId?.ToString() ?? string.Empty;

    /// <summary>The same filter with the product choice replaced by what a query string or a select said. Anything that is neither "shared" nor an id means every product.</summary>
    public KbListFilter WithProduct(string? value) => value switch
    {
        _ when SharedValue.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase) => this with { ProductId = null, SharedOnly = true, Page = 1 },
        _ when Guid.TryParse(value, out var id) => this with { ProductId = id, SharedOnly = false, Page = 1 },
        _ => this with { ProductId = null, SharedOnly = false, Page = 1 },
    };

    /// <summary>The API request for this filter.</summary>
    public ListKbArticlesRequest ToRequest() =>
        new(ProductId, SharedOnly, IncludeShared: false, Status, CategoryId, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Page, KbDefaults.PageSize);

    /// <summary>The URL of this filter: <c>/kb?product=...&amp;status=Draft&amp;page=2</c>. Page 1 and empty values are left out.</summary>
    public string Uri()
    {
        var query = new Dictionary<string, string?>
        {
            [KbQueryKeys.Product] = SharedOnly ? SharedValue : ProductId?.ToString(),
            [KbQueryKeys.Category] = CategoryId?.ToString(),
            [KbQueryKeys.Status] = Status,
            [KbQueryKeys.Search] = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            [KbQueryKeys.Page] = Page > 1 ? Page.ToString(CultureInfo.InvariantCulture) : null,
        };
        return QueryHelpers.AddQueryString("/kb", query.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary());
    }
}
