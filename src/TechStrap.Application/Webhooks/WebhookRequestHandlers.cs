using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Application.Webhooks;

public interface IGetWebhookSettingsRequestHandler
{
    Task<Result<WebhookSettingsDto>> HandleAsync(Guid? productId, CancellationToken cancellationToken);
}
public sealed class GetWebhookSettingsRequestHandler(ICurrentAgentClaims claims, IAgentRepository agents, IWebhookStore store) : IGetWebhookSettingsRequestHandler
{
    public async Task<Result<WebhookSettingsDto>> HandleAsync(Guid? productId, CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, cancellationToken);
        return actor.IsFailure ? Result<WebhookSettingsDto>.Failure(actor.Errors[0]) : await store.GetAsync(productId, cancellationToken);
    }
}
public interface IUpdateWebhookSettingsRequestHandler
{
    Task<Result<WebhookSettingsDto>> HandleAsync(Guid? productId, UpdateWebhookSettingsRequest request, CancellationToken cancellationToken);
}
public sealed class UpdateWebhookSettingsRequestHandler(ICurrentAgentClaims claims, IAgentRepository agents, IWebhookStore store) : IUpdateWebhookSettingsRequestHandler
{
    public async Task<Result<WebhookSettingsDto>> HandleAsync(Guid? productId, UpdateWebhookSettingsRequest request, CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, cancellationToken);
        return actor.IsFailure ? Result<WebhookSettingsDto>.Failure(actor.Errors[0]) : await store.UpdateAsync(productId, request, actor.Value.Id, cancellationToken);
    }
}
public interface IListWebhookDeliveriesRequestHandler
{
    Task<Result<WebhookDeliveryPageDto>> HandleAsync(Guid? productId, int page, CancellationToken cancellationToken);
}
public sealed class ListWebhookDeliveriesRequestHandler(ICurrentAgentClaims claims, IAgentRepository agents, IWebhookStore store) : IListWebhookDeliveriesRequestHandler
{
    public async Task<Result<WebhookDeliveryPageDto>> HandleAsync(Guid? productId, int page, CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, cancellationToken);
        return actor.IsFailure ? Result<WebhookDeliveryPageDto>.Failure(actor.Errors[0]) : await store.ListAsync(productId, page, cancellationToken);
    }
}
public interface IWebhookActionRequestHandler
{
    Task<Result<WebhookActionResponse>> HandleAsync(Guid? productId, WebhookActionRequest request, CancellationToken cancellationToken);
}
public sealed class WebhookActionRequestHandler(ICurrentAgentClaims claims, IAgentRepository agents, IWebhookStore store) : IWebhookActionRequestHandler
{
    public async Task<Result<WebhookActionResponse>> HandleAsync(Guid? productId, WebhookActionRequest request, CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, cancellationToken);
        return actor.IsFailure ? Result<WebhookActionResponse>.Failure(actor.Errors[0]) : await store.ActAsync(productId, request, actor.Value.Id, cancellationToken);
    }
}
