using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The help-centre search (P09-T13): a plain GET form (<c>?q=&amp;page=</c>) that works without script. An empty text shows a prompt and makes no call; a text is cut at the API's limit (<see cref="KbSearchText"/>) and
/// searched through <see cref="IPublicKbClient"/>; no result shows a way to contact support. Review Focus 1 (XSS): the text, every title and every snippet are plain text shown by Razor, which encodes them; the snippet is
/// never markup (D-044). A page with a text is <c>noindex</c> (every text is a different page), the page is never kept by the cache (<c>PortalCachePaths</c>) or a browser (a header rule), and a paging link keeps the
/// text, escaped by <see cref="PortalRoutes.KbSearch(string, string, int)"/>. The page number is bound as text and parsed by <see cref="KbPaging"/>.
/// </summary>
public partial class KbSearch : ProductPageBase
{
    public const int PageSize = KbLimits.DefaultPublicSearchPageSize;

    [SupplyParameterFromQuery(Name = PortalRoutes.QueryParameter)]
    private string? Query { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.PageParameter)]
    private string? PageText { get; set; }

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    /// <summary>The cleaned search text: empty when there is none.</summary>
    private string Text { get; set; } = string.Empty;

    private PagedResponse<PublicKbSearchResultDto>? Results { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        Text = KbSearchText.Clean(Query);
        if (Text.Length == 0)
        {
            return;
        }

        var page = KbPaging.Parse(PageText);
        var result = await Kb.SearchAsync(Key, Text, page, PageSize, RequestAborted);
        if (result.IsSuccess)
        {
            if (page > 1 && result.Value.Items.Count == 0 && result.Value.TotalCount > 0)
            {
                // A page past the end of results that exist: the same neutral 404 as a category page past its end, not a "No articles found" for a crawler to find at every page number.
                NotFoundAfterTheming();
                return;
            }

            Results = result.Value;
            return;
        }

        Fail(result.Errors[0]);
    }

    private static IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme) =>
        [new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)), new KbCrumb(KbCopy.SearchHeading)];
}
