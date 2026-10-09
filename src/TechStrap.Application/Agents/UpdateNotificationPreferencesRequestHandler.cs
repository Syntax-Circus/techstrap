using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Rules;

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
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure(me.Errors[0]);
        }

        if (request.Preferences is null)
        {
            return Result.Failure(new ResultError("notification-preferences-required", "Send the list of product preferences.", ResultErrorKind.Validation, "preferences"));
        }

        if (request.Preferences.Count > DomainLimits.NotificationPreferencesMaxCount)
        {
            return Result.Failure(new ResultError("notification-preferences-too-many", $"Send at most {DomainLimits.NotificationPreferencesMaxCount} product preferences at once.", ResultErrorKind.Validation, "preferences"));
        }

        var seen = new HashSet<Guid>();
        for (var index = 0; index < request.Preferences.Count; index++)
        {
            var preference = request.Preferences[index];
            if (preference is null)
            {
                return Result.Failure(new ResultError("notification-preference-required", "Each preference needs a product and a choice.", ResultErrorKind.Validation, $"preferences[{index}]"));
            }

            if (!seen.Add(preference.ProductId))
            {
                return Result.Failure(new ResultError("notification-product-repeated", "Each product can appear only once.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }
        }

        var existing = await products.GetExistingIdsAsync(request.Preferences.Select(p => p.ProductId).ToList(), cancellationToken);
        for (var index = 0; index < request.Preferences.Count; index++)
        {
            if (!existing.Contains(request.Preferences[index].ProductId))
            {
                return Result.Failure(new ResultError("notification-product-unknown", "That product does not exist. Reload the list and try again.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }
        }

        foreach (var preference in request.Preferences)
        {
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(me.Value.Id, preference.ProductId, preference.NotifyNewTicket), cancellationToken);
        }

        return await scope.CommitAsync(cancellationToken);
    }
}
