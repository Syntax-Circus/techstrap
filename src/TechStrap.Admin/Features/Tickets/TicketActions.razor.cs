using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

public enum ActionDialog
{
    None,
    Spam,
    Delete,
    Erase,
}

/// <summary>
/// The overflow menu of a ticket and the dialogs behind it, in the three tiers of UX-BRIEF-admin. Not spam is reversible and has no dialog. Mark as spam asks for a confirmation that names
/// the ticket. Delete and erase are Admin-only, irreversible, and need the ticket number or the requester's email typed: for an Agent they are not hidden but not rendered at all (no menu
/// entry, no dialog, nothing to trigger), and the API enforces the same rule. A failure leaves the dialog open and everything unchanged; only success navigates. A write is never cancelled
/// when the component goes away, and a failure that leaves the outcome unknown says so and offers a reload instead of a retry.
/// </summary>
public sealed partial class TicketActions : IDisposable
{
    private const string QueuePath = "/queue";

    private ActionDialog _dialog;
    private bool _menuOpen;
    private bool _busy;
    private bool _disposed;
    private bool _uncertain;
    private bool _notSpamUncertain;
    private string? _error;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private IRequestersClient Requesters { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;

    /// <summary>Raised with the API's state after Not spam; the page replaces its model from it.</summary>
    [Parameter]
    public EventCallback<TicketStateDto> OnState { get; set; }

    [Parameter]
    public EventCallback OnConflict { get; set; }

    [Parameter]
    public EventCallback OnGone { get; set; }

    /// <summary>Raised when the agent asks to reload after a write whose outcome is unknown; the page reloads silently.</summary>
    [Parameter]
    public EventCallback OnReload { get; set; }

    // Spam on a Closed ticket is refused by the API (409 ticket-closed), so the entries are not offered there.
    private bool CanMarkSpam => !Ticket.IsSpam && !Ticket.IsClosed;

    private bool CanRestore => Ticket.IsSpam && !Ticket.IsClosed;

    private bool HasAnyAction => CanMarkSpam || CanRestore || Session.IsAdmin;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    private void ToggleMenu() => _menuOpen = !_menuOpen;

    private void Open(ActionDialog dialog)
    {
        _menuOpen = false;
        _error = null;
        _uncertain = false;
        _dialog = dialog;
    }

    private void Close()
    {
        if (!_busy)
        {
            _dialog = ActionDialog.None;
            _error = null;
            _uncertain = false;
        }
    }

    private async Task ReloadAsync()
    {
        _dialog = ActionDialog.None;
        _error = null;
        _uncertain = false;
        _notSpamUncertain = false;
        await OnReload.InvokeAsync();
    }

    private Task ConfirmSpamAsync() =>
        RunAsync(
            ct => Tickets.SetSpamAsync(Ticket.Id, new MarkTicketSpamRequest(true, Ticket.RowVersion), ct),
            _ => LeaveForQueue(ActionsCopy.MarkedSpam(Ticket.Number)),
            ActionsCopy.SpamFailed,
            ActionsCopy.SpamUncertain);

    private Task ConfirmDeleteAsync() =>
        RunAsync(
            async ct => Lift(await Tickets.DeleteAsync(Ticket.Id, ct)),
            _ => LeaveForQueue(ActionsCopy.Deleted(Ticket.Number)),
            ActionsCopy.DeleteFailed,
            ActionsCopy.DeleteUncertain);

    private Task ConfirmEraseAsync() =>
        RunAsync(
            async ct => Lift(await Requesters.EraseAsync(Ticket.Requester.Id, ct)),
            _ => LeaveForQueue(ActionsCopy.Erased(Ticket.Number)),
            ActionsCopy.EraseFailed,
            ActionsCopy.EraseUncertain);

    private async Task RestoreAsync()
    {
        _menuOpen = false;
        await RunAsync(
            ct => Tickets.SetSpamAsync(Ticket.Id, new MarkTicketSpamRequest(false, Ticket.RowVersion), ct),
            async state =>
            {
                StatusMessages.Show(ActionsCopy.Restored(Ticket.Number));
                await OnState.InvokeAsync(state);
            },
            ActionsCopy.NotSpamFailed,
            ActionsCopy.RestoreUncertain,
            dialogOpen: false);
    }

    /// <summary>Runs one request. Success runs <paramref name="onSuccess"/>; a conflict or a missing ticket is handed to the page; an unknown outcome says so; anything else is shown and nothing changes.</summary>
    private async Task RunAsync<T>(Func<CancellationToken, Task<Result<T>>> send, Func<T, Task> onSuccess, Func<string, string> failure, string uncertain, bool dialogOpen = true)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        _uncertain = false;
        _notSpamUncertain = false;
        StateHasChanged();
        try
        {
            // A write is never cancelled: it may already be applied, so the screen closing must not abandon it half-sent.
            var result = await send(CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _dialog = ActionDialog.None;
                await onSuccess(result.Value);
                return;
            }

            await HandleFailureAsync(result.Errors[0], failure, uncertain, dialogOpen);
        }
        finally
        {
            _busy = false;
        }
    }

    // Delete and erase answer with a plain Result; this lets them share the one request runner.
    private static Result<bool> Lift(Result result) => result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Errors[0]);

    private async Task HandleFailureAsync(ResultError error, Func<string, string> failure, string uncertain, bool dialogOpen)
    {
        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _dialog = ActionDialog.None;
            await OnConflict.InvokeAsync();
        }
        else if (error.Code == ApiErrorCodes.TicketNotFound)
        {
            _dialog = ActionDialog.None;
            await OnGone.InvokeAsync();
        }
        else if (error.Code is ApiErrorCodes.ApiTimeout or ApiErrorCodes.ApiUnavailable or ApiErrorCodes.UnexpectedResponse or ApiErrorCodes.ApiError)
        {
            // The write may have been applied before the answer was lost: never a bare "try again", and nothing claims it was left unchanged.
            if (dialogOpen)
            {
                _error = uncertain;
                _uncertain = true;
            }
            else
            {
                _notSpamUncertain = true;
            }
        }
        else if (dialogOpen)
        {
            _error = failure(error.Message);
        }
        else
        {
            StatusMessages.Show(failure(error.Message));
        }
    }

    private Task LeaveForQueue(string message)
    {
        StatusMessages.Show(message);
        Navigation.NavigateTo(QueuePath);
        return Task.CompletedTask;
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.NotSpam && CanRestore && !_busy)
        {
            await InvokeAsync(async () =>
            {
                await RestoreAsync();
                StateHasChanged();
            });
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
    }
}
