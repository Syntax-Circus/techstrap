using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IPublishKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles/{id}/publish (Agent). The signed-in agent must be active. Needs a title, a slug, a body and a category (the portal address carries the category slug); a
/// missing one is a 400 <c>kb-publish-incomplete</c> whose target names the field. The first publication date is kept when an Archived article is
/// published again. The caller may send the version they last read; a different stored version is a 409, so they never publish text they have not seen.
/// A stored body over the rendered element cap (it may predate the cap) is refused with <c>kb-body-too-complex</c> and the article is left unchanged.
/// </summary>
public sealed class PublishKbArticleRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IKbRepository knowledgeBase,
    IKbContentRenderer renderer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IPublishKbArticleRequestHandler
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

        if (renderer.IsTooComplex(article.BodyMarkdown))
        {
            return Result<KbArticleDto>.Failure(KbErrors.BodyTooComplex());
        }

        var published = article.Publish(clock);
        if (published.IsFailure)
        {
            return Result<KbArticleDto>.Failure(published.Error!.ToError());
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
