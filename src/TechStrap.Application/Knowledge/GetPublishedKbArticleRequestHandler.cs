using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetPublishedKbArticleRequestHandler
{
    Task<Result<PublishedKbArticleDto>> HandleAsync(string? productKey, string? categorySlug, string? slug, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug} (anonymous, D-044). Renders with the same <see cref="IKbContentRenderer"/> as the Admin preview.
/// Everything that is not a Published article of this product's scope under that category (unknown or inactive product, draft, archived, another
/// product's article, wrong category) is the same 404. The DTO has no author and no ids.
/// </summary>
public sealed class GetPublishedKbArticleRequestHandler(IProductRepository products, IKbRepository knowledgeBase, IKbContentRenderer renderer) : IGetPublishedKbArticleRequestHandler
{
    public async Task<Result<PublishedKbArticleDto>> HandleAsync(string? productKey, string? categorySlug, string? slug, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || string.IsNullOrWhiteSpace(categorySlug) || string.IsNullOrWhiteSpace(slug))
        {
            return Result<PublishedKbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        var view = await knowledgeBase.GetPublicArticleAsync(product.Id, categorySlug.Trim(), slug.Trim(), cancellationToken);
        if (view is null || view.Article.PublishedAt is not { } publishedAt)
        {
            return Result<PublishedKbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        var article = view.Article;
        return Result<PublishedKbArticleDto>.Success(new PublishedKbArticleDto(
            view.ProductKey, view.CategorySlug, view.CategoryName, article.Slug, article.Title, article.Summary, renderer.Render(article.BodyMarkdown), publishedAt, article.UpdatedAt));
    }
}
