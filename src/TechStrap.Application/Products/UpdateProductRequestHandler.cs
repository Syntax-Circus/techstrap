using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

public interface IUpdateProductRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/products/{id} (Admin). The caller sends the Version they read; a different stored version is a 409 so a second
/// admin's edit is never silently overwritten (D-026). The repository checks it again at commit.
/// </summary>
public sealed class UpdateProductRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateProductRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var input = request.Branding;
        var branding = ProductBranding.Create(input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo);
        if (branding.IsFailure)
        {
            return Result<ProductDto>.Failure(branding.Error!.ToError());
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        if (product.Version != request.Version)
        {
            return Result<ProductDto>.Failure(ProductErrors.Stale());
        }

        var changed = new List<string>();
        if (!string.Equals(product.Name, request.Name?.Trim(), StringComparison.Ordinal))
        {
            changed.Add("name");
        }

        if (product.Branding != branding.Value)
        {
            changed.Add("branding");
        }

        if (product.IsActive != request.IsActive)
        {
            changed.Add("isActive");
        }

        var updated = product.UpdateDetails(request.Name, branding.Value);
        if (updated.IsFailure)
        {
            return Result<ProductDto>.Failure(updated.Error!.ToError());
        }

        if (changed.Count == 0)
        {
            return Result<ProductDto>.Success(ProductMapping.ToDto(product));
        }

        product.SetActive(request.IsActive);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }

        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved));
    }
}
