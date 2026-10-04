using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Email;

public sealed record PurgeEmailOutboxResult(int Deleted);

public interface IPurgeEmailOutboxHandler
{
    Task<Result<PurgeEmailOutboxResult>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Deletes Sent and Discarded outbox rows older than <see cref="OutboxRetentionOptions.Days"/> (D-039), one batch per call. DeadLettered,
/// Pending and Sending rows are never touched. Logs counts only.
/// </summary>
public sealed class PurgeEmailOutboxHandler(
    IEmailOutboxStore store, TimeProvider clock, IOptions<OutboxRetentionOptions> options, ILogger<PurgeEmailOutboxHandler> logger) : IPurgeEmailOutboxHandler
{
    public async Task<Result<PurgeEmailOutboxResult>> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(settings.Days);
        var deleted = await store.DeleteFinishedBeforeAsync(cutoff, settings.BatchSize, cancellationToken);
        logger.Log(deleted == 0 ? LogLevel.Debug : LogLevel.Information, "Outbox retention deleted {Deleted} finished rows.", deleted);
        return Result<PurgeEmailOutboxResult>.Success(new PurgeEmailOutboxResult(deleted));
    }
}
