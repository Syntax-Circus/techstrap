using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The help-center home of a product (P09-T12): the categories it can see, each with its published article count and description, through <see cref="IPublicKbClient"/>. The product loads first (the base class): an unknown,
/// inactive or malformed product is the neutral 404 before any KB call is made. A product with no category shows the empty state, not an error. Every name and description is plain text and encoded.
/// </summary>
public partial class KbHome : ProductPageBase
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
        var categories = Kb.ListCategoriesAsync(Key, RequestAborted);
        var newest = Kb.ListRecentArticlesAsync(Key, KbLimits.DefaultRecentArticlesCount, RequestAborted);
        var result = await categories;
        var recent = await newest;
        if (result.IsSuccess)
        {
            Categories = result.Value;

            // The newest articles are an extra: when they cannot be read the list is left out and the categories stay.
            if (recent.IsSuccess)
            {
                Recent = recent.Value;
            }

            return;
        }

        Fail(result.Errors[0]);
    }

    // Generated per page, never the product-wide override: the product home carries that one, and two pages of a site should not share a description.
    private static string Description(ProductThemeViewModel theme, IReadOnlyList<PublicKbCategoryDto> categories) =>
        SeoText.KbHomeDescription(theme.DisplayName, categories.Select(c => c.Name).ToList(), categories.Sum(c => c.ArticleCount));

    private IReadOnlyList<object> StructuredData(ProductThemeViewModel theme, IReadOnlyList<PublicKbCategoryDto> categories) =>
        KbStructuredData.ForKbHome(Links.Absolute, Links.KbHome(theme.Key), SeoText.Title(KbCopy.HomeTitle(theme.DisplayName)), Description(theme, categories), Crumbs(theme));

    private IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme) =>
        [new KbCrumb(theme.DisplayName, Links.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading)];
}
