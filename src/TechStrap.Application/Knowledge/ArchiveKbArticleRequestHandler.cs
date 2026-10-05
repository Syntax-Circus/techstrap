using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IArchiveKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles/{id}/archive (Agent). The signed-in agent must be active. Takes the article out of public search, the article page, the sitemap and the category counts. The row
/// and its first publication date stay. Archiving an Archived article is a 409. The version is optional, as for publish.
/// </summary>
public sealed class ArchiveKbArticleRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IArchiveKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbArticleDto>.Failure(actor.Errors[0]);
        }

        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        if (article is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        if (version is { } expected && expected != article.Version)
        {
            return Result<KbArticleDto>.Failure(KbErrors.Stale("article"));
        }

        var archived = article.Archive(clock);
        if (archived.IsFailure)
        {
            return Result<KbArticleDto>.Failure(archived.Error!.ToError());
        }

        knowledgeBase.UpdateArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbArticleDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
