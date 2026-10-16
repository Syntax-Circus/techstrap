using TechStrap.Application.Webhooks;

namespace TechStrap.Worker.Outbox;

public sealed class WebhookOutboxWorker(IServiceScopeFactory scopes, ILogger<WebhookOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var count = 0;
            try
            {
                using var scope = scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IDrainWebhookOutboxHandler>();
                count = await handler.HandleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Webhook outbox processing failed; retrying after the poll interval."); }
            if (count == 0)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
