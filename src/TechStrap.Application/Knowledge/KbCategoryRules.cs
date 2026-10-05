using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

internal static class KbCategoryRules
{
    /// <summary>
    /// The category and article product match (D-044), a handler duty because the schema has no composite key for it. A shared article
    /// (no product) may only use a shared category; a product article may use a shared category or one of its own product. A missing
    /// category id passes (the article simply has none yet). Returns the error to give back, or null when the category is acceptable.
    /// </summary>
    public static async Task<ResultError?> CheckAsync(IKbRepository kb, Guid? categoryId, Guid? articleProductId, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        var category = await kb.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return KbErrors.CategoryNotFoundInBody();
        }

        return category.ProductId is null || category.ProductId == articleProductId ? null : KbErrors.CategoryScopeMismatch();
    }
}
