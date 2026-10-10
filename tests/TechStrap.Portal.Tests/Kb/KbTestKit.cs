using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// What the help-center host tests share: the paperplane product behind the stub API (<see cref="FormTestKit"/>, whose factory asserts that every API call carried the visitor's address), the three API paths the pages
/// read, builders for the DTOs, and a parsed document, so a test asserts on elements and attributes, not on strings of markup.
/// </summary>
internal static class KbTestKit
{
    public const string CategoriesPath = "/api/public/kb/paperplane/categories";
    public const string CategoryArticlesPath = "/api/public/kb/paperplane/categories/accounts/articles";
    public const string SearchPath = "/api/public/kb/paperplane/search";
    public const string ProductPath = "/api/public/products/paperplane";

    public static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    public static PortalFactory Factory(string environment = "Development") => FormTestKit.Factory(environment);

    public static IHtmlDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    public static async Task<(HttpResponseMessage Response, string Html, IHtmlDocument Dom)> GetAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(path, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response, html, Parse(html));
    }

    public static PublicKbArticleSummaryDto Article(
        string slug = "reset-password", string title = "Reset your password", string? summary = "How to reset it", string category = "accounts", string categoryName = "Accounts", string? product = "paperplane") =>
        new(slug, title, summary, category, categoryName, product, Updated);

    public static PagedResponse<PublicKbArticleSummaryDto> Page(int page, int pageSize, int total, params PublicKbArticleSummaryDto[] items) => new(items, page, pageSize, total);

    public static PublicKbSearchResultDto Hit(string slug = "reset-password", string title = "Reset your password", string snippet = "Use the reset link.", string category = "accounts", string categoryName = "Accounts") =>
        new(slug, title, snippet, category, categoryName, "paperplane");

    public static PagedResponse<PublicKbSearchResultDto> Hits(int page, int pageSize, int total, params PublicKbSearchResultDto[] items) => new(items, page, pageSize, total);

    public static void Problem(PortalFactory factory, string path, HttpStatusCode status, string code = "kb-category-not-found") =>
        factory.Api.OnProblem(HttpMethod.Get, path, status, code, "Whatever the API says: never shown to a visitor.");

    public static string[] Texts(IHtmlDocument dom, string selector) => [.. dom.QuerySelectorAll(selector).Select(element => element.TextContent.Trim())];

    public static string[] Links(IHtmlDocument dom, string selector) => [.. dom.QuerySelectorAll(selector).Select(element => element.GetAttribute("href") ?? string.Empty)];

    public static string Meta(IHtmlDocument dom, string selector) => dom.QuerySelector(selector)?.GetAttribute("content") ?? string.Empty;

    public static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];
}
