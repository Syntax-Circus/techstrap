using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Products;

public interface ICreateProductRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(CreateProductRequest request, CancellationToken cancellationToken);
}

/// <summary>POST /api/products (Admin, D-022). Key and number prefix are permanent; the accent is checked for format only (D-031).</summary>
public sealed class CreateProductRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateProductRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        ProductBranding? branding = null;
        if (request.Branding is { } input)
        {
            var built = ProductBranding.Create(input.DisplayName, input.LogoPath, input.AccentColour, input.FromAddress, input.ReplyTo);
            if (built.IsFailure)
            {
                return Result<ProductDto>.Failure(built.Error!.ToError());
            }

            branding = built.Value;
        }

        var created = Product.Create(request.Key, request.Name, request.NumberPrefix, branding, clock);
        if (created.IsFailure)
        {
            return Result<ProductDto>.Failure(created.Error!.ToError());
        }

        // The shape is checked before the repository is asked, so a malformed name never costs a query; the host is stored lower-case and compared as stored.
        if (!HostNameShape.TryNormalize(request.PortalHost, out var host))
        {
            return Result<ProductDto>.Failure(ProductErrors.HostInvalid());
        }

        if (host is not null && await products.IsPortalHostTakenAsync(host, exceptProductId: null, cancellationToken))
        {
            return Result<ProductDto>.Failure(ProductErrors.HostTaken());
        }

        var product = created.Value;
        product.SetPortalHost(host);
        products.Add(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductCreated, actor.Value, AdminSubjectType.Product, product.Id,
            new { productKey = product.Key, numberPrefix = product.NumberPrefix }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<ProductDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? ProductErrors.KeyTaken() : committed.Errors[0]);
        }

        // Re-read so the returned Version is the stored concurrency token.
        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved));
    }
}
