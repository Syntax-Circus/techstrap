using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;
using TechStrap.Domain.Outbox;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Webhooks;

/// <summary>Claims due rows atomically across workers. No endpoint URL or response body enters logs or history.</summary>
public sealed class WebhookDeliveryProcessor(TechStrapDbContext context, IWebhookSender sender, TimeProvider clock, IOptions<OutboxRetentionOptions> retention) : IWebhookDeliveryQueue
{
    private const int BatchSize = 20;
    // Longer than a complete batch of bounded HTTP requests. Expired claims may be recovered by another worker.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var claim = Guid.NewGuid();
        var until = now + Lease;
        var batch = await context.Set<WebhookDeliveryRecord>().FromSqlInterpolated($"""
            UPDATE webhook_deliveries SET status = 'Sending', attempts = attempts + 1, claim_id = {claim}, locked_until = {until}
            WHERE id IN (
                SELECT id FROM webhook_deliveries
                WHERE (status = 'Pending' AND next_attempt_at <= {now}) OR (status = 'Sending' AND locked_until <= {now})
                ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT {BatchSize}
            ) RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        foreach (var delivery in batch)
        {
            var status = "Discarded";
            string? error = null;
            var next = now;
            if (await WebhookStore.IsActiveAsync(context, delivery, cancellationToken))
            {
                if (delivery.Attempts > OutboxRetryPolicy.MaxAttempts) { status = "DeadLettered"; error = "lease-expired"; }
                else
                {
                    WebhookSendResult result;
                    try
                    {
                        var payload = JsonSerializer.Deserialize<WebhookEventDto>(delivery.Payload, WebhookJson.Options)!;
                        result = await sender.SendAsync(delivery.Url, delivery.Format, payload, cancellationToken);
                    }
                    catch (JsonException) { result = new(false, false, "payload-invalid"); }
                    status = result.Success ? "Sent" : result.Retryable && delivery.Attempts < OutboxRetryPolicy.MaxAttempts ? "Pending" : "DeadLettered";
                    error = result.Error;
                    var backoff = OutboxRetryPolicy.DelayAfter(delivery.Attempts);
                    var delay = result.RetryAfter is { } retry && retry > backoff ? retry : backoff;
                    next = clock.GetUtcNow() + delay;
                }
            }
            // Compare-and-set: a disabled endpoint or a recovered lease must not be resurrected by a late completion.
            var finalStatus = status;
            await context.Set<WebhookDeliveryRecord>().Where(x => x.Id == delivery.Id && x.ClaimId == claim && x.Status == "Sending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, finalStatus).SetProperty(x => x.LastError, error)
                    .SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LockedUntil, (DateTimeOffset?)null).SetProperty(x => x.ClaimId, (Guid?)null)
                    .SetProperty(x => x.Url, finalStatus == "Sent" || finalStatus == "Discarded" ? "" : delivery.Url), CancellationToken.None);
        }
        return batch.Count;
    }

    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var options = retention.Value;
        if (!options.Enabled) return;
        var before = clock.GetUtcNow().AddDays(-options.Days);
        var ids = context.Set<WebhookDeliveryRecord>().Where(x => (x.Status == "Sent" || x.Status == "Discarded") && x.CreatedAt < before)
            .OrderBy(x => x.CreatedAt).Take(options.BatchSize).Select(x => x.Id);
        await context.Set<WebhookDeliveryRecord>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
