using Microsoft.Extensions.Options;
using TechStrap.Application.Email;

namespace TechStrap.Worker.Outbox;

/// <summary>
/// Deletes finished email outbox rows after the retention window (D-039). One DI scope per iteration. A full batch runs again at once;
/// otherwise, and after an unexpected error, the loop waits the configured interval.
/// </summary>
public sealed class OutboxRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OutboxRetentionOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxRetentionWorker> _logger;

    public OutboxRetentionWorker(IServiceScopeFactory scopes, IOptions<OutboxRetentionOptions> options, TimeProvider clock, ILogger<OutboxRetentionWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox retention worker is disabled.");
            return;
        }

        var idle = TimeSpan.FromMinutes(_options.IntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool more;
            try
            {
                more = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox retention run failed; retrying in {Delay}.", idle);
                more = false;
            }

            if (more)
            {
                continue;
            }

            try
            {
                await Task.Delay(idle, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IPurgeEmailOutboxHandler>();
        var result = await handler.HandleAsync(cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Outbox retention returned {Code}.", result.Errors[0].Code);
            return false;
        }

        return result.Value.Deleted >= _options.BatchSize;
    }
}
