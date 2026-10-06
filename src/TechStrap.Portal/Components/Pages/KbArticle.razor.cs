using Microsoft.AspNetCore.Components;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// One published article (P09-T14). Review Focus 2: an unpublished article, another product's, a wrong category, an unknown slug and an unknown product are the one neutral 404, byte for byte (the product is forgotten first),
/// and a slug that is not a slug is answered without a call (the client refuses it). Review Focus 1: the body is the API's sanitised HTML shown by <see cref="KbArticleBody"/>, the one place that renders it; the title, the
/// category, the summary and every meta value are plain text and encoded; the structured data is written through <see cref="JsonLdText"/>. The canonical address is the product path the visitor is on (a shared article is
/// canonical under each product, D-045). <c>/p/{key}/kb/search/{slug}</c> matches this route with the category <c>search</c>, which the API refuses (it is reserved), so it is a 404 here too.
/// </summary>
public partial class KbArticle : ProductPageBase
{
    [Parameter]
    public string Category { get; set; } = string.Empty;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    [Inject]
    private ISeoUrlBuilder Urls { get; set; } = default!;

    private PublishedKbArticleDto? Article { get; set; }

    private string Description { get; set; } = string.Empty;

    private IReadOnlyList<KbCrumb> Trail { get; set; } = [];

    private IReadOnlyList<object> StructuredData { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not { } theme)
        {
            return;
        }

        var result = await Kb.GetArticleAsync(Key, Category, Slug, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }

        var article = result.Value;
        Description = KbPlainText.Describe(article.Summary, article.Html);
        if (Description.Length == 0)
        {
            Description = KbCopy.ArticleDescriptionFallback(article.Title, theme.DisplayName);
        }

        Trail =
        [
            new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)),
            new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)),
            new KbCrumb(article.CategoryName, PortalRoutes.KbCategory(theme.Key, article.CategorySlug)),
            new KbCrumb(article.Title),
        ];
        StructuredData = KbStructuredData.ForArticle(Urls.AbsoluteUrl, theme, article, Description, Trail);
        Article = article;
    }
}
