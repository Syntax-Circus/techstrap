using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>GET /api/kb/articles/{id} (Agent). Any status, with the Markdown source and the concurrency version.</summary>
public sealed class GetKbArticleRequestHandler(IKbRepository knowledgeBase) : IGetKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        return article is null ? Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound()) : Result<KbArticleDto>.Success(KbMapping.ToDto(article));
    }
}
