using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The number of dead-lettered emails, for the badge on the "Failed emails" link. Only an admin session may call <see cref="RefreshAsync"/>: the call is a 403 for a
/// plain agent. <c>NavMenu</c> refreshes it once when an admin session is ready; the dead letters page calls <see cref="Set"/> or <see cref="RefreshAsync"/> after it
/// retries or discards a row, so the badge follows. <see cref="Count"/> is null until a refresh has succeeded; a failed refresh keeps the last known number (a badge is not
/// worth an error message). Scoped: one per circuit.
/// </summary>
public sealed class FailedEmailCounter(IDeadLettersClient deadLetters)
{
    /// <summary>The last known number of dead-lettered emails, or null when it has not been read.</summary>
    public int? Count { get; private set; }

    /// <summary>Raised after <see cref="Count"/> changed or a refresh finished.</summary>
    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await deadLetters.CountAsync(cancellationToken);
        if (result.IsSuccess)
        {
            Count = result.Value;
        }

        Changed?.Invoke();
    }

    /// <summary>For a page that already holds the total (the dead letters list does), so the badge needs no second call.</summary>
    public void Set(int count)
    {
        Count = Math.Max(0, count);
        Changed?.Invoke();
    }
}
