using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Knowledge;

public interface IListKbArticlesRequestHandler
{
    Task<Result<PagedResponse<KbArticleListItemDto>>> HandleAsync(ListKbArticlesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/kb/articles (Agent). Newest update first, or best match first when <c>text</c> is set (full-text search over title, summary and
/// body). Every status is listed unless the request names one. The product filter includes shared articles unless the request turns that off.
/// </summary>
public sealed class ListKbArticlesRequestHandler(IKbRepository knowledgeBase) : IListKbArticlesRequestHandler
{
    public async Task<Result<PagedResponse<KbArticleListItemDto>>> HandleAsync(ListKbArticlesRequest request, CancellationToken cancellationToken)
    {
        if (!KbMapping.TryParseStatus(request.Status, out var status))
        {
            return Result<PagedResponse<KbArticleListItemDto>>.Failure(KbErrors.StatusInvalid());
        }

        var page = string.IsNullOrWhiteSpace(request.Text)
            ? await knowledgeBase.ListArticlesAsync(
                new KbArticleQuery(request.ProductId, request.IncludeShared, status, request.CategoryId, request.Page, request.PageSize, request.SharedOnly),
                cancellationToken)
            : await knowledgeBase.SearchAsync(
                new KbSearchQuery(request.Text, request.ProductId, request.IncludeShared, status, request.CategoryId, request.Page, request.PageSize, request.SharedOnly),
                cancellationToken);

        return Result<PagedResponse<KbArticleListItemDto>>.Success(
            new PagedResponse<KbArticleListItemDto>([.. page.Items.Select(KbMapping.ToListItem)], page.Page, page.PageSize, page.TotalCount));
    }
}
