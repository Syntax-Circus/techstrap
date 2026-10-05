using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

public interface IDeleteKbCategoryRequestHandler
{
    Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/kb/categories/{id} (Admin, D-022). The signed-in agent must be active. A category that still holds articles, in any status, is a 409 <c>kb-category-in-use</c>; there is no
/// reassign flow. The database foreign key backs the check up, so a race ends in the same answer.
/// </summary>
public sealed class DeleteKbCategoryRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork) : IDeleteKbCategoryRequestHandler
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var category = await knowledgeBase.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return Result.Failure(KbErrors.CategoryNotFound());
        }

        if (await knowledgeBase.CategoryHasArticlesAsync(category.Id, cancellationToken))
        {
            return Result.Failure(KbErrors.CategoryInUse());
        }

        knowledgeBase.RemoveCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure(committed.Errors[0].Code == PersistenceErrorCodes.ReferenceViolation ? KbErrors.CategoryInUse() : committed.Errors[0]);
        }

        return Result.Success();
    }
}
