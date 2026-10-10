using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Products;

public interface IUploadProductLogoRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(Guid productId, IncomingProductLogo? logo, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/products/{id}/logo (Admin). The signed-in admin must be active and the product must exist, checked before anything is stored. No file is a 400 <c>file-required</c>. The store checks the size and the type by the leading
/// bytes and picks the name; the product row then holds that name, which supersedes the linked logo address (D-052). If the commit fails the new file is the orphan and is deleted; the previous file is deleted only after a successful commit.
/// </summary>
public sealed class UploadProductLogoRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IProductLogoStore store,
    IProductLogoUrls logoUrls,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUploadProductLogoRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(Guid productId, IncomingProductLogo? logo, CancellationToken cancellationToken)
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

        if (logo is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.LogoFileRequired());
        }

        var stored = await store.SaveAsync(logo, cancellationToken);
        if (stored.IsFailure)
        {
            return Result<ProductDto>.Failure(stored.Errors[0]);
        }

        var previous = product.Branding.UploadedLogo;
        product.SetUploadedLogo(stored.Value.FileName);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed = new List<string> { "uploadedLogo" } }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            // The row did not change, so the new file is the orphan: remove it. The request token may be gone; the delete is best effort on its own token.
            await store.DeleteAsync(stored.Value.FileName, CancellationToken.None);
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }

        if (previous is not null)
        {
            await store.DeleteAsync(previous, CancellationToken.None);
        }

        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved, logoUrls));
    }
}
