using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

public interface ICreateKbCategoryRequestHandler
{
    Task<Result<KbCategoryDto>> HandleAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/categories (Agent). The signed-in agent must be active. The slug and the product are permanent. The slug must be free here and in the other scope (D-044), and
/// <c>search</c> is reserved (a Domain rule). A null product makes the category shared.
/// </summary>
public sealed class CreateKbCategoryRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateKbCategoryRequestHandler
{
    public async Task<Result<KbCategoryDto>> HandleAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(actor.Errors[0]);
        }

        var created = KbCategory.Create(request.ProductId, request.Slug, request.Name, request.SortOrder, clock, request.Description);
        if (created.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(created.Error!.ToError());
        }

        var category = created.Value;
        if (category.ProductId is { } productId && await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.ProductNotFoundInBody());
        }

        if (await knowledgeBase.CategorySlugTakenAsync(category.ProductId, category.Slug, cancellationToken))
        {
            return Result<KbCategoryDto>.Failure(KbErrors.CategorySlugTaken());
        }

        knowledgeBase.AddCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? KbErrors.CategorySlugTaken() : committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetCategoryAsync(category.Id, cancellationToken) ?? category;
        return Result<KbCategoryDto>.Success(KbMapping.ToDto(saved));
    }
}
