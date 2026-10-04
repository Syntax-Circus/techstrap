namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The one-line confirmations in the status bar ("Reply sent on ACME-142"). Scoped, so there is one per circuit. A message stays until the next
/// one replaces it. It supplements, and never replaces, an inline error for a failure that needs action.
/// </summary>
public sealed class StatusMessageService
{
    public string? Current { get; private set; }

    public event Action? Changed;

    public void Show(string message)
    {
        Current = message;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
