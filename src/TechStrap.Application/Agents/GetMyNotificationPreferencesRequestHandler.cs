using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;

namespace TechStrap.Application.Agents;

public interface IGetMyNotificationPreferencesRequestHandler
{
    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/agents/me/notification-preferences: every active product with the caller's choice (off when never set).</summary>
public sealed class GetMyNotificationPreferencesRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products) : IGetMyNotificationPreferencesRequestHandler
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result<IReadOnlyList<NotificationPreferenceDto>>.Failure(me.Errors[0]);
        }

        var chosen = (await agents.ListNotificationPreferencesAsync(me.Value.Id, cancellationToken))
            .ToDictionary(preference => preference.ProductId, preference => preference.NotifyNewTicket);
        var active = await products.ListAsync(activeOnly: true, cancellationToken);
        IReadOnlyList<NotificationPreferenceDto> preferences =
            [.. active.Select(product => new NotificationPreferenceDto(product.Id, product.Name, chosen.GetValueOrDefault(product.Id)))];
        return Result<IReadOnlyList<NotificationPreferenceDto>>.Success(preferences);
    }
}
