using Microsoft.Extensions.Options;
using TechStrap.Application.Email;

namespace TechStrap.Worker.Outbox;

/// <summary>
/// Drains the email outbox (D-010, D-033). One DI scope per iteration (the outbox store clears its change tracker and runs its own
/// transactions). Keeps going while batches come back non-empty; waits the poll interval when idle or after an unexpected error.
/// </summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private const int MachineNameMaxLength = 32;

    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxWorkerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(IServiceScopeFactory scopes, IOptions<EmailOutboxWorkerOptions> options, TimeProvider clock, ILogger<EmailOutboxWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;

        // claimed_by is varchar(100); a generated id is at most 32 + 1 + 32 = 65 characters.
        WorkerId = string.IsNullOrWhiteSpace(_options.WorkerId)
            ? $"{Truncate(Environment.MachineName, MachineNameMaxLength)}-{Guid.NewGuid():N}"
            : _options.WorkerId.Trim();
    }

    public string WorkerId { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Email outbox worker is disabled.");
            return;
        }

        var idle = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool more;
            try
            {
                more = await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Only database and infrastructure failures get here: the SMTP adapter turns every send failure into a category.
                _logger.LogError(ex, "Email outbox drain failed; retrying in {Delay}.", idle);
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

    private async Task<bool> DrainOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IDrainEmailOutboxHandler>();
        var result = await handler.HandleAsync(WorkerId, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Email outbox drain returned {Code}.", result.Errors[0].Code);
            return false;
        }

        return result.Value.Claimed > 0;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
