using SyntaxCircus.Common;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Application.Webhooks;

/// <summary>Admin configuration and operations. Implementations enforce scoped endpoint ownership.</summary>
public interface IWebhookStore
{
    Task<Result<WebhookSettingsDto>> GetAsync(Guid? productId, CancellationToken cancellationToken);
    Task<Result<WebhookSettingsDto>> UpdateAsync(Guid? productId, UpdateWebhookSettingsRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<Result<WebhookDeliveryPageDto>> ListAsync(Guid? productId, int page, CancellationToken cancellationToken);
    Task<Result<WebhookActionResponse>> ActAsync(Guid? productId, WebhookActionRequest request, Guid actorId, CancellationToken cancellationToken);
}
