using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>
/// The knowledge base for the portal (PHASE-08, PHASE-09, D-044): anonymous, rate limited per IP. A hit is cacheable for 60 seconds (the sitemap for 300);
/// a 404 is never cached. An unknown or inactive product key gives an empty list, never a 404, except on the article page, where everything unavailable is one 404.
/// </summary>
[ApiController]
[Route("api/public/kb/{productKey}")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
public sealed class PublicKbController : ControllerBase
{
    public const int HitMaxAgeSeconds = 60;
    public const int SitemapMaxAgeSeconds = 300;

    /// <summary>Published articles of the product and the shared space, best match first, 10 a page (at most 25). <c>category</c> is an optional category slug.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        string productKey,
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromServices] ISearchPublicKbArticlesRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = KbLimits.DefaultPublicSearchPageSize) =>
        CachedFor(HitMaxAgeSeconds, (await handler.HandleAsync(productKey, q, category, page, pageSize, cancellationToken)));

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(string productKey, [FromServices] IListPublicKbCategoriesRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, cancellationToken));

    /// <summary>One Published article as sanitised HTML. A draft, an archived article, another product's article, a wrong category and an unknown key are the same 404.</summary>
    [HttpGet("articles/{categorySlug}/{slug}")]
    public async Task<IActionResult> Article(
        string productKey, string categorySlug, string slug, [FromServices] IGetPublishedKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, slug, cancellationToken));

    /// <summary>One page of a category's Published articles (the product's and the shared ones), newest update first, 10 a page (at most 25). An unknown, invisible or empty category is a 404.</summary>
    [HttpGet("categories/{categorySlug}/articles")]
    public async Task<IActionResult> CategoryArticles(
        string productKey,
        string categorySlug,
        [FromServices] IListPublicKbCategoryArticlesRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = KbLimits.DefaultPublicSearchPageSize) =>
        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, page, pageSize, cancellationToken));

    [HttpGet("sitemap")]
    public async Task<IActionResult> Sitemap(string productKey, [FromServices] IGetKbSitemapRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(SitemapMaxAgeSeconds, await handler.HandleAsync(productKey, cancellationToken));

    // no-store first, so an error stays uncacheable; a success then asks for the short public cache.
    private IActionResult CachedFor<T>(int seconds, SyntaxCircus.Common.Result<T> result)
    {
        Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this, value =>
        {
            Response.Headers.CacheControl = $"public, max-age={seconds}";
            return Ok(value);
        });
    }
}
