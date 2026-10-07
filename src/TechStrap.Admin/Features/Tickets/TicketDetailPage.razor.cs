using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Live;
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
    private IDisposable? _palette;
    private bool _liveChanged;
    private int _liveChanges;
    private Guid? _joinedTicket;
    private TicketPresenceDto? _presence;
    private IReadOnlyList<PresenceViewModel> _viewers = [];
    private bool _composingExpired;
    private ITimer? _composingTimer;
    private int _composingGeneration;

    [Inject]
    private TicketDetailPresenter Presenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private CommandRegistry Commands { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ITicketLiveClient LiveClient { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private ILogger<TicketDetailPage> Logger { get; set; } = default!;

    /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
    [Parameter]
    public string Number { get; set; } = string.Empty;

    private sealed record GoneMessage(string Heading, string Body);

    protected override void OnInitialized()
    {
        Shortcuts.Pressed += OnShortcutAsync;
        LiveClient.TicketChanged += OnLiveChange;
        LiveClient.PresenceChanged += OnPresenceChanged;
    }

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
        SyncPalette();
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

        // A change that arrives while this load runs is newer than what it fetches: the banner is cleared only when nothing arrived meanwhile.
        var changesSeen = _liveChanges;

        Result<TicketDetailViewModel> result;
        try
        {
            result = await Presenter.LoadAsync(Number, silent ? _model?.Lookups : null, cts.Token);
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
            SyncPalette();
            _error = string.Empty;
            if (changesSeen == _liveChanges)
            {
                _liveChanged = false;
            }

            SyncLiveGroup();
            return true;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            SyncPalette();
            SyncLiveGroup();
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
            return true;
        }

        // A failed refresh keeps the ticket on screen with an inline alert; a failed first load shows the alert alone.
        _error = $"{TicketCopy.LoadFailed} {error.Message}";
        return true;
    }

    /// <summary>
    /// Another agent, the customer or the Worker changed a ticket (D-046: every change reaches every agent, so the page filters). A change to THIS ticket by someone else, or a resync, only raises the banner:
    /// <c>_model</c>, its row version and the composer's draft stay exactly as they are until the agent clicks it, so a send in between still gets the 409 and nobody acts on a version they have not seen.
    /// The agent's own changes are ignored (their own write already refreshed the page). It runs on a pool thread, so it hops to the renderer first.
    /// </summary>
    private void OnLiveChange(TicketChangedDto change)
    {
        if (_disposed || !LiveClient.IsEnabled)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            if (_disposed || _model is null || !LiveChangeRules.Concerns(change, _model.Id) || LiveChangeRules.IsOwn(change, Session.Agent?.Id))
            {
                return;
            }

            _liveChanges++;
            _liveChanged = true;
            StateHasChanged();
        });
    }

    /// <summary>The click: the ordinary silent refresh, which replaces the timeline, the status and the row version. The draft lives in <c>DraftStore</c> and is not part of it.</summary>
    private Task ApplyLiveRefreshAsync() => RefreshAsync();

    private void OnPresenceChanged(TicketPresenceDto presence)
    {
        if (_disposed || !LiveClient.IsEnabled)
        {
            return;
        }

        _ = InvokeAsync(() => ApplyPresence(presence));
    }

    private void ApplyPresence(TicketPresenceDto? presence)
    {
        if (_disposed || presence is null || presence.TicketId != _joinedTicket)
        {
            return;
        }

        _presence = presence;
        _composingExpired = false;
        RebuildViewers();
        ArmComposingTimer();
        StateHasChanged();
    }

    private void RebuildViewers() => _viewers = PresenceViewModelFactory.Create(_presence, Session.Agent?.Id, _composingExpired);

    /// <summary>
    /// The hub does not tell us when a "replying" hint lapses (its lease is 10 seconds and the composer refreshes it every 4): after the lease with no news, the hint is shown as "viewing". Every presence message restarts it.
    /// </summary>
    private void ArmComposingTimer()
    {
        _composingGeneration++;
        _composingTimer?.Dispose();
        _composingTimer = null;
        if (PresenceViewModelFactory.AnyoneReplying(_presence, Session.Agent?.Id))
        {
            var generation = _composingGeneration;
            _composingTimer = Time.CreateTimer(_ => _ = InvokeAsync(() => ExpireComposing(generation)), null, TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>A callback queued just before a fresh presence message (or a ticket switch) re-armed the timer is stale: it must neither expire the fresh hint nor dispose the newer timer.</summary>
    private void ExpireComposing(int generation)
    {
        if (_disposed || generation != _composingGeneration)
        {
            return;
        }

        _composingTimer?.Dispose();
        _composingTimer = null;

        _composingExpired = true;
        RebuildViewers();
        StateHasChanged();
    }

    /// <summary>
    /// Keeps the hub group in step with the ticket on screen: joins after a load (the model has the id the hub needs), leaves the old one when the route moves to another ticket or the ticket is gone.
    /// A prerender instance records the join on a client that is never started, which does nothing.
    /// </summary>
    private void SyncLiveGroup()
    {
        var wanted = LiveClient.IsEnabled ? _model?.Id : null;
        if (wanted == _joinedTicket)
        {
            return;
        }

        var previous = _joinedTicket;
        _joinedTicket = wanted;
        _presence = null;
        _viewers = [];
        _composingExpired = false;
        _composingGeneration++;
        _composingTimer?.Dispose();
        _composingTimer = null;
        _liveChanged = false;
        if (previous is { } old)
        {
            _ = LeaveGroupAsync(old);
        }

        if (wanted is { } joined)
        {
            _ = JoinGroupAsync(joined);
        }
    }

    private async Task JoinGroupAsync(Guid ticketId)
    {
        try
        {
            var presence = await LiveClient.JoinTicketAsync(ticketId);
            if (!_disposed)
            {
                await InvokeAsync(() => ApplyPresence(presence));
            }
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Joining the live group of a ticket failed ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private async Task LeaveGroupAsync(Guid ticketId)
    {
        try
        {
            await LiveClient.LeaveTicketAsync(ticketId);
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Leaving the live group of a ticket failed ({ExceptionType}).", exception.GetType().Name);
        }
    }

    /// <summary>Replaces the status fields and RowVersion from a write's response. Used by the composer, the sidebar and the actions.</summary>
    internal void ApplyState(TicketStateDto state)
    {
        if (_model is not null)
        {
            _model = _model.WithState(state);
            SyncPalette();
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

    /// <summary>
    /// Offers this ticket's commands in the command palette while the ticket is on screen: reply and note on an open ticket, "Assign to me" when it is not already assigned to the agent,
    /// and "Not spam" on a flagged one. Each raises the shortcut that does the same thing, so the owner of the action (the composer, the sidebar, the actions menu) runs its own code.
    /// Called whenever the model changes, so the list never offers what the ticket no longer allows.
    /// </summary>
    private void SyncPalette()
    {
        _palette?.Dispose();
        _palette = null;
        if (_disposed || _model is not { IsClosed: false } ticket)
        {
            return;
        }

        List<PaletteCommand> commands =
        [
            new("ticket-reply", PaletteCopy.ReplyCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.Reply), Keys: "r"),
            new("ticket-note", PaletteCopy.NoteCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.Note), Keys: "n"),
        ];
        if (Session.Agent is { } me && ticket.AssigneeId != me.Id)
        {
            commands.Add(new("ticket-assign-me", PaletteCopy.AssignToMeCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.AssignToMe)));
        }

        if (ticket.IsSpam)
        {
            commands.Add(new("ticket-not-spam", PaletteCopy.NotSpamCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.NotSpam), Keys: "u"));
        }

        _palette = Commands.Register(commands);
    }

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
        _palette?.Dispose();
        Shortcuts.Pressed -= OnShortcutAsync;
        LiveClient.TicketChanged -= OnLiveChange;
        LiveClient.PresenceChanged -= OnPresenceChanged;
        _composingTimer?.Dispose();
        if (_joinedTicket is { } joined)
        {
            _joinedTicket = null;
            _ = LeaveGroupAsync(joined);
        }

        _lifetime.Cancel();
        _load?.Dispose();
        _lifetime.Dispose();
    }
}
