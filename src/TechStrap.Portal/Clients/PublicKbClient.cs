using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicKbClient(ApiConnection api) : IPublicKbClient
{
    public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("pageSize", Number(pageSize))),
                cancellationToken)
            : NotFound<PagedResponse<PublicKbSearchResultDto>>();

    public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int page, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("page", Number(page)), ("pageSize", Number(pageSize))),
                cancellationToken)
            : NotFound<PagedResponse<PublicKbSearchResultDto>>();

    public Task<Result<IReadOnlyList<PublicKbCategoryDto>>> ListCategoriesAsync(string productKey, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<IReadOnlyList<PublicKbCategoryDto>>($"api/public/kb/{productKey}/categories", cancellationToken)
            : NotFound<IReadOnlyList<PublicKbCategoryDto>>();

    public Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> ListCategoryArticlesAsync(
        string productKey, string categorySlug, int page, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug)
            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(
                ApiQuery.Build($"api/public/kb/{productKey}/categories/{categorySlug}/articles", ("page", Number(page)), ("pageSize", Number(pageSize))),
                cancellationToken)
            : NotFound<PagedResponse<PublicKbArticleSummaryDto>>();

    public Task<Result<PublishedKbArticleDto>> GetArticleAsync(string productKey, string categorySlug, string slug, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug) && KbSlugShape.IsWellFormed(slug)
            ? api.GetAsync<PublishedKbArticleDto>($"api/public/kb/{productKey}/articles/{categorySlug}/{slug}", cancellationToken)
            : NotFound<PublishedKbArticleDto>();

    public Task<Result<IReadOnlyList<KbSitemapEntryDto>>> GetSitemapAsync(string productKey, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<IReadOnlyList<KbSitemapEntryDto>>($"api/public/kb/{productKey}/sitemap", cancellationToken)
            : NotFound<IReadOnlyList<KbSitemapEntryDto>>();

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Task<Result<T>> NotFound<T>() => Task.FromResult(Result<T>.Failure(ProblemMapping.NotFound()));
}
