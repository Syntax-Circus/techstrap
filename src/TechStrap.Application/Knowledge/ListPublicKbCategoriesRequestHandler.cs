using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IListPublicKbCategoriesRequestHandler
{
    Task<Result<IReadOnlyList<PublicKbCategoryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/categories (anonymous, D-044). The product's categories and the shared ones, each with the number of Published
/// articles the product can see in it; a category with none is left out. Drafts and archived articles are never counted. An unknown or inactive product gives an empty list.
/// </summary>
public sealed class ListPublicKbCategoriesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IListPublicKbCategoriesRequestHandler
{
    public async Task<Result<IReadOnlyList<PublicKbCategoryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null)
        {
            return Result<IReadOnlyList<PublicKbCategoryDto>>.Success([]);
        }

        var categories = await knowledgeBase.ListPublicCategoriesAsync(product.Id, cancellationToken);
        return Result<IReadOnlyList<PublicKbCategoryDto>>.Success(
            [.. categories.Select(item => new PublicKbCategoryDto(item.Category.Slug, item.Category.Name, item.Category.Description, item.ArticleCount))]);
    }
}
