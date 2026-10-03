using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.ApiKeys;

public interface IRevokeProductApiKeyRequestHandler
{
    Task<Result> HandleAsync(Guid productId, Guid keyId, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/products/{id}/api-keys/{keyId} (Admin): revokes a key; revoking twice is harmless. Kind and secret never change:
/// revoke and create a new key instead.
/// </summary>
public sealed class RevokeProductApiKeyRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRevokeProductApiKeyRequestHandler
{
    public async Task<Result> HandleAsync(Guid productId, Guid keyId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var key = await products.GetApiKeyAsync(keyId, cancellationToken);
        if (key is null || key.ProductId != productId)
        {
            return Result.Failure(ApiKeyMapping.NotFound());
        }

        if (key.IsRevoked)
        {
            return Result.Success();
        }

        key.Revoke(clock);
        products.UpdateApiKey(key);
        AdminAudit.Record(adminEvents, AdminEventType.ApiKeyRevoked, actor.Value, AdminSubjectType.ApiKey, key.Id,
            new { productId, keyPrefix = key.KeyPrefix }, clock);

        return await scope.CommitAsync(cancellationToken);
    }
}
