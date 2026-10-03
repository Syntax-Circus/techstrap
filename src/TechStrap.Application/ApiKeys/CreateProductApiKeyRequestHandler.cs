using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Results;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

public interface ICreateProductApiKeyRequestHandler
{
    Task<Result<CreateProductApiKeyResponse>> HandleAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/products/{id}/api-keys (Admin, D-001, D-022). The plaintext key is returned once, in this response only; the
/// database keeps the prefix and hash, and the audit records the prefix.
/// </summary>
public sealed class CreateProductApiKeyRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IApiKeyHasher apiKeyHasher,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateProductApiKeyRequestHandler
{
    public async Task<Result<CreateProductApiKeyResponse>> HandleAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<CreateProductApiKeyResponse>.Failure(actor.Errors[0]);
        }

        if (!ApiKeyMapping.TryParseKind(request.Kind, out var kind))
        {
            return Result<CreateProductApiKeyResponse>.Failure(ApiKeyMapping.KindInvalid());
        }

        if (await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<CreateProductApiKeyResponse>.Failure(ProductErrors.NotFound());
        }

        var generated = apiKeyHasher.Generate(kind);
        var created = ProductApiKey.Create(productId, kind, generated.KeyPrefix, generated.KeyHash, request.Label, clock);
        if (created.IsFailure)
        {
            return Result<CreateProductApiKeyResponse>.Failure(created.Error!.ToError());
        }

        products.AddApiKey(created.Value);
        AdminAudit.Record(adminEvents, AdminEventType.ApiKeyCreated, actor.Value, AdminSubjectType.ApiKey, created.Value.Id,
            new { productId, kind = kind.ToString(), keyPrefix = generated.KeyPrefix }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsSuccess
            ? Result<CreateProductApiKeyResponse>.Success(new CreateProductApiKeyResponse(ApiKeyMapping.ToDto(created.Value), generated.PlaintextKey))
            : Result<CreateProductApiKeyResponse>.Failure(committed.Errors[0]);
    }
}
