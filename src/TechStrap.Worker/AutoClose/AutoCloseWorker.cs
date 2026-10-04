using Microsoft.Extensions.Options;
using TechStrap.Application.Tickets.AutoClose;

namespace TechStrap.Worker.AutoClose;

/// <summary>
/// Closes Solved tickets after the configured days (D-008, D-037). One DI scope per iteration. A full batch runs again at once;
/// otherwise, and after an unexpected error, the loop waits the configured interval.
/// </summary>
public sealed class AutoCloseWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AutoCloseOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutoCloseWorker> _logger;

    public AutoCloseWorker(IServiceScopeFactory scopes, IOptions<AutoCloseOptions> options, TimeProvider clock, ILogger<AutoCloseWorker> logger)
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
            _logger.LogInformation("Auto-close worker is disabled.");
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
                _logger.LogError(ex, "Auto-close run failed; retrying in {Delay}.", idle);
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
        var handler = scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>();
        var result = await handler.HandleAsync(cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Auto-close returned {Code}.", result.Errors[0].Code);
            return false;
        }

        return result.Value.Closed + result.Value.Conflicts >= _options.BatchSize;
    }
}
