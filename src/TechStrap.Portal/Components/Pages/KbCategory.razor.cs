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
/// One category of a product's help centre (P09-T12): a page of its published articles, newest update first, <see cref="PageSize"/> to a page, with plain <c>?page=n</c> links that work without script. Review Focus 2:
/// an unknown category, another product's category, an empty one, a slug that is not a slug and a page past the end are all the neutral 404, byte for byte the page an unknown route gets (the product is forgotten first), and
/// a slug that is not a slug is answered without a call (the client refuses it: <see cref="IPublicKbClient"/>; a check of the page's own was dead code, as a surviving mutation showed). The page number is bound as text and parsed by <see cref="KbPaging"/>, because the framework's own number binding answers 500 for <c>?page=abc</c>.
/// </summary>
public partial class KbCategory : ProductPageBase
{
    public const int PageSize = KbLimits.DefaultPublicSearchPageSize;

    [Parameter]
    public string Category { get; set; } = string.Empty;

    [SupplyParameterFromQuery(Name = PortalRoutes.PageParameter)]
    private string? PageText { get; set; }

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    private PagedResponse<PublicKbArticleSummaryDto>? Listing { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        var result = await Kb.ListCategoryArticlesAsync(Key, Category, KbPaging.Parse(PageText), PageSize, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }

        if (result.Value.Items.Count == 0)
        {
            // A page past the end: the same 404 as a category that does not exist (a 200 for every page number would be a page per number for a crawler to find).
            NotFoundAfterTheming();
            return;
        }

        Listing = result.Value;
    }

    private IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme, string categoryName) =>
        [new KbCrumb(theme.DisplayName, Links.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading, Links.KbHome(theme.Key)), new KbCrumb(categoryName)];
}
