using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUpdateKbCategoryRequestHandler
{
    Task<Result<KbCategoryDto>> HandleAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/kb/categories/{id} (Agent). The signed-in agent must be active. Changes the name, description and sort order. The caller sends the version they read; a stale one is a 409 (D-026).</summary>
public sealed class UpdateKbCategoryRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork) : IUpdateKbCategoryRequestHandler
{
    public async Task<Result<KbCategoryDto>> HandleAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(actor.Errors[0]);
        }

        var category = await knowledgeBase.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.CategoryNotFound());
        }

        if (category.Version != request.Version)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.Stale("category"));
        }

        var updated = category.Update(request.Name, request.Description, request.SortOrder);
        if (updated.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(updated.Error!.ToError());
        }

        knowledgeBase.UpdateCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetCategoryAsync(category.Id, cancellationToken) ?? category;
        return Result<KbCategoryDto>.Success(KbMapping.ToDto(saved));
    }
}
