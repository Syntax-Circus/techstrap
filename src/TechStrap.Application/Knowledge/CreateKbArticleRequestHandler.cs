using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

public interface ICreateKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(CreateKbArticleRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles (Agent). Creates a Draft authored by the signed-in agent. A null product makes the article shared. The slug must
/// be free in the article's own scope and in the other one (D-044); the category must fit the article's scope. A body over the rendered
/// element cap is refused (<see cref="IKbContentRenderer.IsTooComplex"/>). KB changes are not audited.
/// </summary>
public sealed class CreateKbArticleRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IKbRepository knowledgeBase,
    IKbContentRenderer renderer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(CreateKbArticleRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbArticleDto>.Failure(actor.Errors[0]);
        }

        var created = KbArticle.Create(request.ProductId, request.CategoryId, request.Slug, request.Title, request.Summary, request.BodyMarkdown, actor.Value.Id, clock);
        if (created.IsFailure)
        {
            return Result<KbArticleDto>.Failure(created.Error!.ToError());
        }

        var article = created.Value;
        if (article.BodyMarkdown.Length > 0 && renderer.IsTooComplex(article.BodyMarkdown))
        {
            return Result<KbArticleDto>.Failure(KbErrors.BodyTooComplex());
        }

        if (article.ProductId is { } productId && await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ProductNotFoundInBody());
        }

        if (await KbCategoryRules.CheckAsync(knowledgeBase, article.CategoryId, article.ProductId, cancellationToken) is { } categoryError)
        {
            return Result<KbArticleDto>.Failure(categoryError);
        }

        if (await knowledgeBase.ArticleSlugTakenAsync(article.ProductId, article.Slug, cancellationToken))
        {
            return Result<KbArticleDto>.Failure(KbErrors.SlugTaken());
        }

        knowledgeBase.AddArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            // Two agents creating the same slug at once: the unique index decides, and the loser sees the same answer as the pre-check.
            return Result<KbArticleDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? KbErrors.SlugTaken() : committed.Errors[0]);
        }

        // Reload so the DTO carries the version the database assigned.
        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
