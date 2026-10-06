using System.Text.Json.Serialization;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Seo;

/// <summary>One step of a <see cref="BreadcrumbListLd"/>: its place in the trail, its name and the absolute address it stands for.</summary>
public sealed record BreadcrumbItemLd(
    int Position,
    JsonLdText Name,
    JsonLdText Item,
    [property: JsonPropertyName("@type")] string Type = "ListItem");

/// <summary>
/// schema.org <c>BreadcrumbList</c>. The package has a record of the same shape, but its strings are plain strings that its serialiser writes without escaping <c>&lt;</c>; this one carries <see cref="JsonLdText"/>.
/// </summary>
public sealed record BreadcrumbListLd(
    IReadOnlyList<BreadcrumbItemLd> ItemListElement,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "BreadcrumbList");

/// <summary>A schema.org <c>WebPage</c> reference, for <c>mainEntityOfPage</c>.</summary>
public sealed record WebPageLd(
    [property: JsonPropertyName("@id")] JsonLdText Id,
    [property: JsonPropertyName("@type")] string Type = "WebPage");

/// <summary>A schema.org <c>Organization</c> reference (the product), for <c>author</c> and <c>publisher</c>.</summary>
public sealed record OrganizationLd(JsonLdText Name, [property: JsonPropertyName("@type")] string Type = "Organization");

/// <summary>
/// schema.org <c>Article</c> for a help article (the package has none). Every string is a <see cref="JsonLdText"/>. The address of the page is the canonical one, which for a shared article is the product path the visitor is on.
/// </summary>
public sealed record ArticleSchema(
    JsonLdText Headline,
    JsonLdText Description,
    DateTimeOffset DatePublished,
    DateTimeOffset DateModified,
    WebPageLd MainEntityOfPage,
    JsonLdText Image,
    OrganizationLd Author,
    OrganizationLd Publisher,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "Article");

/// <summary>Builds the structured data of an article page: the breadcrumb trail and the article, each safe to write into a script block.</summary>
public static class KbStructuredData
{
    /// <param name="absoluteUrl">Turns a root-relative path into the absolute address (<c>ISeoUrlBuilder.AbsoluteUrl</c>).</param>
    public static IReadOnlyList<object> ForArticle(
        Func<string, string> absoluteUrl, ProductThemeViewModel theme, PublishedKbArticleDto article, string description, IReadOnlyList<KbCrumb> trail)
    {
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(article);
        ArgumentNullException.ThrowIfNull(trail);
        var pageUrl = absoluteUrl(PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug));
        var product = new OrganizationLd(JsonLdText.Safe(theme.DisplayName));
        var items = trail
            .Select((crumb, index) => new BreadcrumbItemLd(index + 1, JsonLdText.Safe(crumb.Label), JsonLdText.Safe(crumb.Href is { } href ? absoluteUrl(href) : pageUrl)))
            .ToList();
        return
        [
            new BreadcrumbListLd(items),
            new ArticleSchema(
                JsonLdText.Safe(article.Title),
                JsonLdText.Safe(description),
                article.PublishedAt,
                article.UpdatedAt,
                new WebPageLd(JsonLdText.Safe(pageUrl)),
                JsonLdText.Safe(absoluteUrl(KbSeo.Image(theme))),
                product,
                product),
        ];
    }
}
