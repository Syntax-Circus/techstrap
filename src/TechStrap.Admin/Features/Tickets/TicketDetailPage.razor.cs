using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// One ticket: header, one chronological timeline (messages and changes), and the side panel. The page owns the model and its RowVersion; the children raise callbacks
/// and the page replaces the model from the server's answer (never from what the user typed). A refresh keeps the current model on screen while it runs, and a ticket
/// deleted meanwhile becomes "This ticket no longer exists". Esc, when nothing is being typed, goes back to the queue.
/// </summary>
public sealed partial class TicketDetailPage : IDisposable
{
    private const string QueuePath = "/queue";

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private string? _loadedNumber;
    private GoneMessage? _gone;
    private string _error = string.Empty;
    private bool _loading;
    private bool _disposed;
    private TicketDetailViewModel? _model;
    private ConflictState _conflict;
    private bool _reloading;
    private string? _latestChange;

    [Inject]
    private TicketDetailPresenter Presenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
    [Parameter]
    public string Number { get; set; } = string.Empty;

    private sealed record GoneMessage(string Heading, string Body);

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnParametersSetAsync()
    {
        if (Number == _loadedNumber)
        {
            return;
        }

        _loadedNumber = Number;
        await LoadAsync();
    }

    /// <summary>The full load: a skeleton while it runs. Used for the first load and the Retry after a failed first load.</summary>
    private async Task LoadAsync()
    {
        _loading = true;
        _gone = null;
        _model = null;
        _error = string.Empty;
        await LoadCoreAsync(silent: false);
    }

    /// <summary>The refresh after a write or a conflict: the model on screen stays until the new one arrives.</summary>
    internal async Task RefreshAsync() => await RefreshCoreAsync();

    /// <returns>True when the refresh was applied; false when it was cancelled or superseded by a newer one.</returns>
    private async Task<bool> RefreshCoreAsync()
    {
        var applied = await LoadCoreAsync(silent: true);
        if (applied && !_disposed)
        {
            StateHasChanged();
        }

        return applied;
    }

    /// <returns>False when the load was cancelled or superseded and nothing was applied.</returns>
    private async Task<bool> LoadCoreAsync(bool silent)
    {
        if (_disposed)
        {
            // A write finished after the page was closed; the lifetime source is gone and there is nothing left to refresh.
            return false;
        }

        _load?.Cancel();
        _load?.Dispose();
        var cts = _load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        Result<TicketDetailViewModel> result;
        try
        {
            result = await Presenter.LoadAsync(Number, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return false;
        }

        if (cts.IsCancellationRequested)
        {
            return false;
        }

        _loading = false;
        if (result.IsSuccess)
        {
            _model = result.Value;
            _error = string.Empty;
            return true;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
            return true;
        }

        // A failed refresh keeps the ticket on screen with an inline alert; a failed first load shows the alert alone.
        _error = $"{TicketCopy.LoadFailed} {error.Message}";
        return true;
    }

    /// <summary>Replaces the status fields and RowVersion from a write's response. Used by the composer, the sidebar and the actions.</summary>
    internal void ApplyState(TicketStateDto state)
    {
        if (_model is not null)
        {
            _model = _model.WithState(state);
            StateHasChanged();
        }
    }

    /// <summary>A reply or note was accepted: take the new status and RowVersion from the response at once, then reload so the timeline shows the new entries.</summary>
    private Task OnSentAsync(AgentMessageResponse response) => OnStateChangedAsync(response.Ticket);

    /// <summary>Any accepted write: the response replaces the model's status fields and RowVersion, then a reload brings the new timeline entries.</summary>
    private async Task OnStateChangedAsync(TicketStateDto state)
    {
        ApplyState(state);
        await RefreshAsync();
    }

    /// <summary>A write hit 409 concurrency-conflict: nothing was changed, the user's draft and choice stay, and the banner offers a reload.</summary>
    private Task OnConflictAsync()
    {
        _conflict = ConflictState.Stale;
        _latestChange = null;
        return Task.CompletedTask;
    }

    /// <summary>Reload is a silent refresh, so the composer and its files stay mounted.</summary>
    private async Task ReloadAfterConflictAsync()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;
        try
        {
            var applied = await RefreshCoreAsync();
            if (applied && !_disposed && _model is not null && _error.Length == 0)
            {
                var latest = _model.Timeline.LastOrDefault(entry => entry.Kind == TimelineEntryKind.Event);
                _latestChange = latest is null ? null : SidebarCopy.LatestChange(latest.Text, latest.Actor);
                _conflict = ConflictState.Reloaded;
            }
        }
        finally
        {
            _reloading = false;
        }
    }

    private void DismissConflict() => _conflict = ConflictState.None;

    private Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.Escape)
        {
            return InvokeAsync(() => Navigation.NavigateTo(QueuePath));
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _load?.Dispose();
        _lifetime.Dispose();
    }
}
