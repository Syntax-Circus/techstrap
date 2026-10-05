using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUpdateKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/kb/articles/{id} (Agent). Replaces the category, title, summary and body. The caller sends the version they read: a different
/// stored version is a 409, so a second agent's edit is never silently overwritten (D-026). The repository checks it again at commit.
/// Editing an Archived article returns it to Draft; editing a Published article keeps it live. The product and the slug never change.
/// A body over the rendered element cap is refused (<see cref="IKbContentRenderer.IsTooComplex"/>) before anything is changed.
/// </summary>
public sealed class UpdateKbArticleRequestHandler(
    IKbRepository knowledgeBase,
    IKbContentRenderer renderer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        if (article is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        if (article.Version != request.Version)
        {
            return Result<KbArticleDto>.Failure(KbErrors.Stale("article"));
        }

        if (!string.IsNullOrEmpty(request.BodyMarkdown) && renderer.IsTooComplex(request.BodyMarkdown))
        {
            return Result<KbArticleDto>.Failure(KbErrors.BodyTooComplex());
        }

        if (await KbCategoryRules.CheckAsync(knowledgeBase, request.CategoryId, article.ProductId, cancellationToken) is { } categoryError)
        {
            return Result<KbArticleDto>.Failure(categoryError);
        }

        var updated = article.Update(request.CategoryId, request.Title, request.Summary, request.BodyMarkdown, clock);
        if (updated.IsFailure)
        {
            return Result<KbArticleDto>.Failure(updated.Error!.ToError());
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
