using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// The queue: six views, filters, search and paging, all in the URL. It loads the list and the per-view counts together, keeps the previous list on screen
/// when a refresh fails, and drives the keyboard selection (<c>j</c>/<c>k</c>, Enter, <c>/</c>). The default view is Unassigned.
/// </summary>
public sealed partial class TicketQueuePage : IAsyncDisposable
{
    private const string ModulePath = "./js/queue.js";

    private QueueFilter _filter = new(QueueDefaults.View, null, null, null, null, null, 1);
    private QueueFilter? _loadedFor;
    private QueueFilterBar? _filterBar;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _cts;
    private IJSObjectReference? _module;
    private PagedResponse<TicketSummaryDto>? _page;
    private IReadOnlyList<TicketRowViewModel> _rows = [];
    private TicketViewCountsResponse? _counts;
    private IReadOnlyList<ProductDto> _products = [];
    private IReadOnlyList<TagDto> _tags = [];
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _lookupsLoaded;
    private bool _scrollSelected;
    private bool _notSpamBusy;
    private bool _notSpamError;
    private bool _disposed;
    private int _selected = -1;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private ITagsClient TagsClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The route segment: <c>unassigned</c>, <c>mine</c>, <c>open</c>, <c>pending</c>, <c>all</c> or <c>spam</c>. Absent means the default view.</summary>
    [Parameter]
    public string? View { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Product)]
    public string? Product { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Status)]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Priority)]
    public string? Priority { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Tag)]
    public string? Tag { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Search)]
    public string? Search { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Page)]
    public string? PageNumber { get; set; }

    private bool IsSpamView => _filter.View == TicketViews.Spam;

    private bool IsCaughtUpView => _filter.View is TicketViews.Unassigned or TicketViews.Mine or TicketViews.Open;

    private string CaughtUpLinkHref => _filter.View == TicketViews.Open
        ? QueueLinks.Uri(_filter with { View = TicketViews.All })
        : QueueLinks.Uri(_filter with { View = TicketViews.Open });

    private string CaughtUpLinkText => _filter.View == TicketViews.Open ? CaughtUpCopy.AllTicketsLink : CaughtUpCopy.OpenTicketsLink;

    private string PlainEmptyHeading => _filter.View switch
    {
        TicketViews.Pending => QueueCopy.NoPendingHeading,
        TicketViews.Spam => QueueCopy.NoSpamHeading,
        _ => QueueCopy.NoTicketsHeading,
    };

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnParametersSetAsync()
    {
        var view = QueueViews.Parse(View);
        if (view is null)
        {
            Navigation.NotFound();
            return;
        }

        // Everything in the query string is untrusted: unparseable or unknown values are dropped rather than sent to the API.
        var filter = new QueueFilter(
            view,
            Guid.TryParse(Product, out var productId) ? productId : null,
            QueueDefaults.Canonical(QueueDefaults.Statuses, Status),
            QueueDefaults.Canonical(QueueDefaults.Priorities, Priority),
            Guid.TryParse(Tag, out var tagId) ? tagId : null,
            Blank(Search),
            int.TryParse(PageNumber, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1);
        _filter = filter;
        if (!_lookupsLoaded)
        {
            _lookupsLoaded = true;
            await LoadLookupsAsync();
        }

        if (filter != _loadedFor)
        {
            _loadedFor = filter;
            _page = null;
            _rows = [];
            _selected = -1;
            await LoadAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scrollSelected)
        {
            return;
        }

        _scrollSelected = false;
        _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _module.InvokeVoidAsync("scrollSelectedIntoView");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task LoadLookupsAsync()
    {
        try
        {
            var products = ProductsClient.ListAsync(_lifetime.Token);
            var tags = TagsClient.ListAsync(_lifetime.Token);
            await Task.WhenAll(products, tags);

            // A failed lookup only means that filter has no options; the queue itself still works.
            _products = products.Result.IsSuccess ? products.Result.Value : [];
            _tags = tags.Result.IsSuccess ? tags.Result.Value : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _products = [];
            _tags = [];
        }
    }

    private async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var filter = _filter;
        _loading = true;
        _error = null;
        _notSpamError = false;

        try
        {
            var list = Tickets.ListAsync(filter.ToRequest(), cts.Token);
            var counts = Tickets.GetCountsAsync(cts.Token);
            await Task.WhenAll(list, counts);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (counts.Result.IsSuccess)
            {
                _counts = counts.Result.Value;
            }

            if (list.Result.IsSuccess)
            {
                ShowPage(list.Result.Value);
            }
            else
            {
                // Keep whatever list is already on screen; the alert explains and offers a retry.
                _error = $"{QueueCopy.LoadFailed} {list.Result.Errors[0].Message}";
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            // Fixed copy only: an exception message can carry a host or a port.
            _error = $"{QueueCopy.LoadFailed} {QueueCopy.TryAgain}";
        }
        finally
        {
            // A newer load owns the flag; a superseded one leaves it alone.
            if (_cts == cts)
            {
                _loading = false;
            }
        }
    }

    private void ShowPage(PagedResponse<TicketSummaryDto> page)
    {
        // A page past the end (a pasted link, or the last tickets on it were handled meanwhile) is not an empty queue: go to the last real page.
        if (page.Items.Count == 0 && (page.TotalCount > 0 || _filter.Page > 1))
        {
            var lastPage = page.TotalCount > 0 ? (int)Math.Ceiling(page.TotalCount / (double)QueueDefaults.PageSize) : 1;
            if (lastPage != _filter.Page)
            {
                Navigation.NavigateTo(QueueLinks.Uri(_filter with { Page = lastPage }), new NavigationOptions { ReplaceHistoryEntry = true });
                return;
            }
        }

        var selectedId = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Id : (Guid?)null;
        _page = page;
        _rows = page.Items.Select(TicketRowViewModel.From).ToList();
        _selected = selectedId is { } id ? _rows.ToList().FindIndex(row => row.Id == id) : -1;
        _announcement = $"{_rows.Count} of {page.TotalCount} tickets";
    }

    private Task RefreshAsync() => LoadAsync();

    private Task RetryAsync() => LoadAsync();

    private string TabHref(string view) => QueueLinks.Uri(_filter with { View = view, Page = 1 });

    private Task OnFilterChangedAsync(QueueFilter filter)
    {
        Navigation.NavigateTo(QueueLinks.Uri(filter), new NavigationOptions { ReplaceHistoryEntry = true });
        return Task.CompletedTask;
    }

    private Task OnPageChangedAsync(int page)
    {
        Navigation.NavigateTo(QueueLinks.Uri(_filter with { Page = page }));
        return Task.CompletedTask;
    }

    private Task ClearFiltersAsync() => OnFilterChangedAsync(_filter.Cleared());

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        await InvokeAsync(async () =>
        {
            switch (action)
            {
                case ShortcutAction.MoveDown:
                    Select(_selected + 1);
                    break;
                case ShortcutAction.MoveUp:
                    Select(_selected - 1);
                    break;
                case ShortcutAction.OpenSelected when _selected >= 0 && _selected < _rows.Count:
                    Navigation.NavigateTo(_rows[_selected].Href);
                    break;
                case ShortcutAction.FocusSearch when _filterBar is not null:
                    await _filterBar.FocusSearchAsync();
                    break;
                case ShortcutAction.NotSpam when IsSpamView && _selected >= 0 && _selected < _rows.Count:
                    await NotSpamAsync(_rows[_selected]);
                    break;
            }

            StateHasChanged();
        });
    }

    /// <summary>
    /// Not spam from the Spam view (the <c>u</c> key or the row button): no dialog, the status is unchanged. A queue row carries no RowVersion (the summary DTO has none) and the API
    /// requires one for this write, so the ticket is read first and the write is made against what was read; a 409 means it changed since and the agent should open it. The write is never
    /// cancelled when the page closes (it may already be applied); a failure with an unknown outcome says so and offers a reload.
    /// </summary>
    private async Task NotSpamAsync(TicketRowViewModel row)
    {
        if (_notSpamBusy)
        {
            return;
        }

        _notSpamBusy = true;
        _notSpamError = false;
        try
        {
            var ticket = await Tickets.GetAsync(row.Number, _lifetime.Token);
            if (ticket.IsFailure)
            {
                _notSpamError = true;
                _error = ActionsCopy.NotSpamFailed(ticket.Errors[0].Message);
                return;
            }

            var state = await Tickets.SetSpamAsync(row.Id, new MarkTicketSpamRequest(false, ticket.Value.RowVersion), CancellationToken.None);
            if (_disposed)
            {
                // The write went through (or not) after the page was closed: nothing here may touch the page's lifetime or state any more.
                return;
            }

            if (state.IsFailure)
            {
                var error = state.Errors[0];
                // The shared error button only reloads the list, so it says "Reload" for every Not spam failure.
                _notSpamError = true;
                _error = error.Code == ApiErrorCodes.ConcurrencyConflict ? ActionsCopy.ChangedMeanwhile(row.Number)
                    : ApiErrorCodes.IsUncertainWrite(error.Code) ? ActionsCopy.RestoreUncertain
                    : ActionsCopy.NotSpamFailed(error.Message);
                return;
            }

            _error = null;
            RemoveRow(row);
            StatusMessages.Show(ActionsCopy.Restored(row.Number));
            var counts = await Tickets.GetCountsAsync(_lifetime.Token);
            if (counts.IsSuccess)
            {
                _counts = counts.Value;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The page was closed mid-request.
        }
        finally
        {
            _notSpamBusy = false;
        }
    }

    private void RemoveRow(TicketRowViewModel row)
    {
        _rows = _rows.Where(r => r.Id != row.Id).ToList();
        if (_page is not null)
        {
            _page = _page with { Items = _page.Items.Where(t => t.Id != row.Id).ToList(), TotalCount = Math.Max(0, _page.TotalCount - 1) };
        }

        _selected = Math.Min(_selected, _rows.Count - 1);
        _announcement = $"{_rows.Count} of {_page?.TotalCount ?? 0} tickets";
    }

    private void Select(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        _scrollSelected = true;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _cts?.Dispose();
        _lifetime.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; nothing left to release.
            }
        }
    }
}
