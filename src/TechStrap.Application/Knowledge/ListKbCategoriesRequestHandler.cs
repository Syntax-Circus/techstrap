using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IListKbCategoriesRequestHandler
{
    Task<Result<IReadOnlyList<KbCategoryDto>>> HandleAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken);
}

/// <summary>GET /api/kb/categories (Agent). Sort order, then name. Without a product every category is listed; with one, its own plus the shared ones unless that is turned off.</summary>
public sealed class ListKbCategoriesRequestHandler(IKbRepository knowledgeBase) : IListKbCategoriesRequestHandler
{
    public async Task<Result<IReadOnlyList<KbCategoryDto>>> HandleAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken)
    {
        var categories = await knowledgeBase.ListCategoriesAsync(productId, includeShared, cancellationToken);
        return Result<IReadOnlyList<KbCategoryDto>>.Success([.. categories.Select(KbMapping.ToDto)]);
    }
}
