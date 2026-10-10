using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Products;

public interface IRemoveProductLogoRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/products/{id}/logo (Admin). Clears the uploaded logo name so the linked logo address, if any, shows again, then deletes the file after a successful commit. Idempotent: with no uploaded logo it changes and audits nothing (D-052).
/// </summary>
public sealed class RemoveProductLogoRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IProductLogoStore store,
    IProductLogoUrls logoUrls,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRemoveProductLogoRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        if (product.Branding.UploadedLogo is not { } previous)
        {
            return Result<ProductDto>.Success(ProductMapping.ToDto(product, logoUrls));
        }

        product.SetUploadedLogo(null);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed = new List<string> { "uploadedLogo" } }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }

        await store.DeleteAsync(previous, CancellationToken.None);
        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved, logoUrls));
    }
}
