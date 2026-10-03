using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateNotificationPreferencesRequestHandler
{
    Task<Result> HandleAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/agents/me/notification-preferences: per-product new-ticket alert opt-in for the caller. Repeating it is harmless.</summary>
public sealed class UpdateNotificationPreferencesRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IUnitOfWork unitOfWork) : IUpdateNotificationPreferencesRequestHandler
{
    public async Task<Result> HandleAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (request.Preferences is null)
        {
            return Result.Failure(new ResultError("notification-preferences-required", "Send the list of product preferences.", ResultErrorKind.Validation, "preferences"));
        }

        var seen = new HashSet<Guid>();
        for (var index = 0; index < request.Preferences.Count; index++)
        {
            var productId = request.Preferences[index].ProductId;
            if (!seen.Add(productId))
            {
                return Result.Failure(new ResultError("notification-product-repeated", "Each product can appear only once.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }

            if (await products.GetByIdAsync(productId, cancellationToken) is null)
            {
                return Result.Failure(new ResultError("notification-product-unknown", "That product does not exist. Reload the list and try again.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure(me.Errors[0]);
        }

        foreach (var preference in request.Preferences)
        {
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(me.Value.Id, preference.ProductId, preference.NotifyNewTicket), cancellationToken);
        }

        return await scope.CommitAsync(cancellationToken);
    }
}
