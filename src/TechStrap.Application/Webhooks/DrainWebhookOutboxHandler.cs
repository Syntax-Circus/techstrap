namespace TechStrap.Application.Webhooks;

public interface IWebhookDeliveryQueue
{
    Task<int> DrainAsync(CancellationToken cancellationToken);
    Task PurgeAsync(CancellationToken cancellationToken);
}
public interface IDrainWebhookOutboxHandler
{
    Task<int> HandleAsync(CancellationToken cancellationToken);
}
public sealed class DrainWebhookOutboxHandler(IWebhookDeliveryQueue queue) : IDrainWebhookOutboxHandler
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var count = await queue.DrainAsync(cancellationToken);
        await queue.PurgeAsync(cancellationToken);
        return count;
    }
}
