using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>One row of the article list, ready to draw. Mapping is simple, so there is no factory (PHASE-08 component boundaries).</summary>
internal sealed record KbArticleRowViewModel(Guid Id, string Href, string Title, string Slug, string ProductName, string CategoryName, string Status, DateTimeOffset UpdatedAt)
{
    public static KbArticleRowViewModel From(KbArticleListItemDto article, IReadOnlyList<ProductDto> products, IReadOnlyList<KbCategoryDto> categories) => new(
        article.Id,
        $"/kb/{article.Id}",
        article.Title,
        article.Slug,
        ProductNameOf(article.ProductId, products),
        article.CategoryId is { } categoryId ? categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? KbCopy.NoCategory : KbCopy.NoCategory,
        article.Status,
        article.UpdatedAt);

    /// <summary>"Shared" for an article of no product; the product's name; and a fixed phrase for a product the lookups did not return.</summary>
    internal static string ProductNameOf(Guid? productId, IReadOnlyList<ProductDto> products) =>
        productId is not { } id ? KbCopy.Shared : products.FirstOrDefault(p => p.Id == id)?.Name ?? KbCopy.UnknownProduct;
}
