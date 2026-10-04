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
    private TicketDetailViewModel? _model;

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
    internal async Task RefreshAsync()
    {
        await LoadCoreAsync(silent: true);
        StateHasChanged();
    }

    private async Task LoadCoreAsync(bool silent)
    {
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
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        _loading = false;
        if (result.IsSuccess)
        {
            _model = result.Value;
            _error = string.Empty;
            return;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
            return;
        }

        // A failed refresh keeps the ticket on screen with an inline alert; a failed first load shows the alert alone.
        _error = $"{TicketCopy.LoadFailed} {error.Message}";
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
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _load?.Dispose();
        _lifetime.Dispose();
    }
}
