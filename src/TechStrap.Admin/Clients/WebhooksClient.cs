using SyntaxCircus.Common;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Admin.Clients;

public interface IWebhooksClient
{
    Task<Result<WebhookSettingsDto>> GetAsync(Guid? productId, CancellationToken cancellationToken);
    Task<Result<WebhookSettingsDto>> UpdateAsync(Guid? productId, UpdateWebhookSettingsRequest request, CancellationToken cancellationToken);
    Task<Result<WebhookDeliveryPageDto>> ListAsync(Guid? productId, int page, CancellationToken cancellationToken);
    Task<Result<WebhookActionResponse>> ActAsync(Guid? productId, WebhookActionRequest request, CancellationToken cancellationToken);
}

internal sealed class WebhooksClient(ApiConnection connection) : IWebhooksClient
{
    private static string Route(Guid? productId) => productId is null ? "api/settings/webhooks" : $"api/products/{productId}/webhooks";
    public Task<Result<WebhookSettingsDto>> GetAsync(Guid? productId, CancellationToken cancellationToken) => connection.GetAsync<WebhookSettingsDto>(Route(productId), cancellationToken);
    public Task<Result<WebhookSettingsDto>> UpdateAsync(Guid? productId, UpdateWebhookSettingsRequest request, CancellationToken cancellationToken) => connection.SendAsync<WebhookSettingsDto>(HttpMethod.Put, Route(productId), request, cancellationToken);
    public Task<Result<WebhookDeliveryPageDto>> ListAsync(Guid? productId, int page, CancellationToken cancellationToken) => connection.GetAsync<WebhookDeliveryPageDto>($"{Route(productId)}/deliveries?page={page}", cancellationToken);
    public Task<Result<WebhookActionResponse>> ActAsync(Guid? productId, WebhookActionRequest request, CancellationToken cancellationToken) => connection.SendAsync<WebhookActionResponse>(HttpMethod.Post, $"{Route(productId)}/actions", request, cancellationToken);
}
