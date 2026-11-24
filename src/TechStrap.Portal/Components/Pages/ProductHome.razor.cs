using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Products;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The home page of a product (what <c>/</c> is on its own host): the intro, the search, the topics, the newest articles and the way to contact support, with a complete SEO head. The product loads first (the base class). The
/// help-center sections are extras: when the KB cannot be read they are left out and the page is still a 200 with its full head, so a hiccup of the KB never turns the front door into an error. Every name, intro and
/// article title is plain text and encoded.
/// </summary>
public partial class ProductHome : ProductPageBase
{
    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    private IReadOnlyList<PublicKbCategoryDto>? Categories { get; set; }

    private IReadOnlyList<PublicKbArticleSummaryDto>? Recent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        // Both reads start together, so the page waits for the slower one and not for the sum.
        var categoriesRead = Kb.ListCategoriesAsync(Key, RequestAborted);
        var recentRead = Kb.ListRecentArticlesAsync(Key, KbLimits.DefaultRecentArticlesCount, RequestAborted);
        var categories = await categoriesRead;
        var recent = await recentRead;
        if (categories.IsSuccess)
        {
            Categories = categories.Value;
        }

        if (recent.IsSuccess)
        {
            Recent = recent.Value;
        }
    }

    private string Description(ProductThemeViewModel theme) =>
        SeoText.Description(theme.SeoDescription, () => SeoText.ProductHomeDescription(theme.DisplayName, (Categories ?? []).Select(c => c.Name).ToList()));

    private IReadOnlyList<object> StructuredData(ProductThemeViewModel theme) =>
        KbStructuredData.ForProductHome(Links.Absolute, Links.ProductHome(theme.Key), Links.KbSearch(theme.Key), theme);
}
