using System.Text.Json.Serialization;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Seo;

/// <summary>One step of a <see cref="BreadcrumbListLd"/>: its place in the trail, its name and the absolute address it stands for.</summary>
public sealed record BreadcrumbItemLd(
    int Position,
    JsonLdText Name,
    JsonLdText Item,
    [property: JsonPropertyName("@type")] string Type = "ListItem");

/// <summary>
/// schema.org <c>BreadcrumbList</c>. The package has a record of the same shape, but its strings are plain strings that its serializer writes without escaping <c>&lt;</c>; this one carries <see cref="JsonLdText"/>.
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

/// <summary>A schema.org <c>SearchAction</c>: the address template of the help-center search (the <c>q</c> parameter) and the name of its placeholder.</summary>
public sealed record SearchActionLd(
    JsonLdText Target,
    [property: JsonPropertyName("query-input")] string QueryInput = "required name=search_term_string",
    [property: JsonPropertyName("@type")] string Type = "SearchAction");

/// <summary>schema.org <c>WebSite</c> for a product's support site, with its search.</summary>
public sealed record WebSiteLd(
    JsonLdText Name,
    JsonLdText Url,
    SearchActionLd PotentialAction,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "WebSite");

/// <summary>schema.org <c>CollectionPage</c> for a help-center home.</summary>
public sealed record CollectionPageLd(
    JsonLdText Name,
    JsonLdText Description,
    JsonLdText Url,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "CollectionPage");

/// <summary>Builds the structured data of an article page: the breadcrumb trail and the article, each safe to write into a script block.</summary>
public static class KbStructuredData
{
    /// <param name="absoluteUrl">Turns a root-relative path into the absolute address (<c>PortalLinks.Absolute</c>, which leaves an absolute address as it is).</param>
    /// <param name="articlePath">The article page's own path or address, built by <c>PortalLinks</c> (the clean path on the product's host).</param>
    public static IReadOnlyList<object> ForArticle(
        Func<string, string> absoluteUrl, string articlePath, ProductThemeViewModel theme, PublishedKbArticleDto article, string description, IReadOnlyList<KbCrumb> trail)
    {
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(articlePath);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(article);
        ArgumentNullException.ThrowIfNull(trail);
        var pageUrl = absoluteUrl(articlePath);
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

    /// <summary>The structured data of a product's home: the site and its search (the search page takes the text in <c>q</c>).</summary>
    public static IReadOnlyList<object> ForProductHome(Func<string, string> absoluteUrl, string homePath, string searchPath, ProductThemeViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(homePath);
        ArgumentNullException.ThrowIfNull(searchPath);
        ArgumentNullException.ThrowIfNull(theme);
        return
        [
            new WebSiteLd(
                JsonLdText.Safe(theme.DisplayName),
                JsonLdText.Safe(absoluteUrl(homePath)),
                new SearchActionLd(JsonLdText.Safe($"{absoluteUrl(searchPath)}?q={{search_term_string}}"))),
        ];
    }

    /// <summary>The structured data of a help-center home: the breadcrumb trail and the collection page.</summary>
    public static IReadOnlyList<object> ForKbHome(Func<string, string> absoluteUrl, string kbPath, string title, string description, IReadOnlyList<KbCrumb> trail)
    {
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(kbPath);
        ArgumentNullException.ThrowIfNull(trail);
        var pageUrl = absoluteUrl(kbPath);
        var items = trail
            .Select((crumb, index) => new BreadcrumbItemLd(index + 1, JsonLdText.Safe(crumb.Label), JsonLdText.Safe(crumb.Href is { } href ? absoluteUrl(href) : pageUrl)))
            .ToList();
        return [new BreadcrumbListLd(items), new CollectionPageLd(JsonLdText.Safe(title), JsonLdText.Safe(description), JsonLdText.Safe(pageUrl))];
    }
}
