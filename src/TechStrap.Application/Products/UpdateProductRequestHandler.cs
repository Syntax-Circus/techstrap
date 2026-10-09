using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

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
    IOptions<PortalLinkOptions> portal,
    IProductLogoUrls logoUrls,
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

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        // The stored branding is passed in so a logo address saved before the logo rule, and left as it is, does not block the edit (ProductBranding.CreateForUpdate).
        var input = request.Branding;
        var branding = ProductBranding.CreateForUpdate(product.Branding, input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo, input?.Tagline);
        if (branding.IsFailure)
        {
            return Result<ProductDto>.Failure(branding.Error!.ToError());
        }

        if (product.Version != request.Version)
        {
            return Result<ProductDto>.Failure(ProductErrors.Stale());
        }

        // null = the caller did not send the field (a pre-11e client): leave the host alone, run no host check.
        // For a supplied value the shape is checked before the repository is asked; the product's own host is excluded from the uniqueness check.
        var host = product.PortalHost;
        if (request.PortalHost is not null)
        {
            if (string.IsNullOrWhiteSpace(request.PortalHost))
            {
                host = null; // explicit clear: no TryNormalize
            }
            else
            {
                if (!HostNameShape.TryNormalize(request.PortalHost, out host))
                {
                    return Result<ProductDto>.Failure(ProductErrors.HostInvalid());
                }

                if (portal.Value.IsDefaultHost(host))
                {
                    return Result<ProductDto>.Failure(ProductErrors.HostReserved());
                }

                if (host is not null && await products.IsPortalHostTakenAsync(host, product.Id, cancellationToken))
                {
                    return Result<ProductDto>.Failure(ProductErrors.HostTaken());
                }
            }
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

        if (!string.Equals(product.PortalHost, host, StringComparison.Ordinal))
        {
            changed.Add("portalHost");
        }

        // null = the caller did not send the field (a 0.2.0 client): the flag stays as stored.
        var listed = request.ListedOnLanding ?? product.ListedOnLanding;
        if (product.ListedOnLanding != listed)
        {
            changed.Add("listedOnLanding");
        }

        var updated = product.UpdateDetails(request.Name, branding.Value);
        if (updated.IsFailure)
        {
            return Result<ProductDto>.Failure(updated.Error!.ToError());
        }

        if (request.PortalHost is not null)
        {
            product.SetPortalHost(host);
        }

        if (changed.Count == 0)
        {
            return Result<ProductDto>.Success(ProductMapping.ToDto(product, logoUrls));
        }

        product.SetActive(request.IsActive);
        product.SetListedOnLanding(listed);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            // A concurrent writer racing past IsPortalHostTakenAsync also lands here (unique index on portal_host); the persistence error is returned as is (D-050 known limit).
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }

        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved, logoUrls));
    }
}
