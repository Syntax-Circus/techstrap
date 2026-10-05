using System.Globalization;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The article list: filters (product, category, status) and search, all in the URL. Every load takes the next <c>_loadId</c> and only the latest load may change the screen, so a slow answer
/// that was overtaken (another filter, a page change) is ignored. A failed refresh keeps the list that is on screen. The page asks the API only for what any agent may read.
/// </summary>
public sealed partial class KbArticleListPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private KbListFilter _filter = KbListFilter.Empty;
    private KbListFilter? _loadedFor;
    private IReadOnlyList<ProductDto> _products = [];
    private IReadOnlyList<KbCategoryDto> _categories = [];
    private IReadOnlyList<KbArticleRowViewModel> _rows = [];
    private PagedResponse<KbArticleListItemDto>? _page;
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _appliedSearch;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _lookupsLoaded;
    private bool _redirecting;
    private bool _disposed;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [SupplyParameterFromQuery(Name = KbQueryKeys.Product)]
    public string? Product { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Category)]
    public string? Category { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Status)]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Search)]
    public string? Search { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Page)]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        // Everything in the query string is untrusted: unparseable or unknown values are dropped rather than sent to the API.
        var filter = KbListFilter.Empty.WithProduct(Product) with
        {
            CategoryId = Guid.TryParse(Category, out var categoryId) ? categoryId : null,
            Status = KbDefaults.CanonicalStatus(Status),
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1,
        };
        _filter = filter;

        // Follow the URL (clear filters, the back button) unless the agent is mid-word: a pending timer means the text is newer than the filter.
        if (_timer is null && filter.Search != _appliedSearch)
        {
            _text = filter.Search ?? string.Empty;
        }

        _appliedSearch = filter.Search;

        if (!_lookupsLoaded)
        {
            _lookupsLoaded = true;
            await LoadLookupsAsync();
        }

        // Read _filter again, not the local: while the lookups were awaited a newer parameter set may have replaced it and already loaded it, and the local would then load (and be remembered as) the older filter.
        if (_filter != _loadedFor)
        {
            _loadedFor = _filter;
            await LoadAsync();
        }
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            var products = ProductsClient.ListAsync(_lifetime.Token);
            var categories = Kb.ListCategoriesAsync(_lifetime.Token);
            await Task.WhenAll(products, categories);

            // A failed lookup only means that filter has no options, and a row shows a fixed phrase for a name it cannot resolve; the list itself still works.
            _products = products.Result.IsSuccess ? products.Result.Value : [];
            _categories = categories.Result.IsSuccess ? categories.Result.Value : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _products = [];
            _categories = [];
        }
    }

    private async Task LoadAsync()
    {
        var loadId = ++_loadId;
        var filter = _filter;
        _loading = true;
        _redirecting = false;
        _error = null;
        try
        {
            var result = await Kb.ListAsync(filter.ToRequest(), _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                ShowPage(result.Value, filter);
            }
            else
            {
                // Keep whatever list is already on screen; the alert explains and offers a retry.
                _error = $"{KbCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // A load that a newer one overtook says nothing, and its fault must not escape either: nothing awaits it but the lifecycle, where it would end the circuit.
            if (loadId == _loadId && !_disposed)
            {
                // Fixed copy only: an exception message can carry a host or a port.
                _error = $"{KbCopy.LoadFailed} {KbCopy.TryAgain}";
            }
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    private void ShowPage(PagedResponse<KbArticleListItemDto> page, KbListFilter filter)
    {
        // A page past the end (a pasted link, or the last articles on it were archived meanwhile) is not an empty list: go to the last real page.
        if (page.Items.Count == 0 && (page.TotalCount > 0 || filter.Page > 1))
        {
            var lastPage = page.TotalCount > 0 ? (int)Math.Ceiling(page.TotalCount / (double)KbDefaults.PageSize) : 1;
            if (lastPage != filter.Page)
            {
                // The list is not drawn for a page that has nothing on it: the loading state stands in until the move lands and loads the last page.
                _redirecting = true;
                Navigation.NavigateTo((filter with { Page = lastPage }).Uri(), new NavigationOptions { ReplaceHistoryEntry = true });
                return;
            }
        }

        _page = page;
        _rows = [.. page.Items.Select(article => KbArticleRowViewModel.From(article, _products, _categories))];
        _announcement = KbCopy.Count(_rows.Count, page.TotalCount);
    }

    private Task ReloadAsync() => LoadAsync();

    private void OnSearchInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(CommitSearchAsync), null, KbDefaults.SearchDebounce, Timeout.InfiniteTimeSpan);
    }

    private Task SubmitSearchAsync() => CommitSearchAsync();

    private Task CommitSearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var search = string.IsNullOrWhiteSpace(_text) ? null : _text.Trim();
        return search == _filter.Search ? Task.CompletedTask : CommitAsync(_filter with { Search = search, Page = 1 });
    }

    private Task OnProductChangedAsync(ChangeEventArgs e) => CommitAsync(_filter.WithProduct(e.Value as string));

    private Task OnCategoryChangedAsync(ChangeEventArgs e) => CommitAsync(_filter with { CategoryId = ParseGuid(e), Page = 1 });

    private Task OnStatusChangedAsync(ChangeEventArgs e) => CommitAsync(_filter with { Status = KbDefaults.CanonicalStatus(e.Value as string), Page = 1 });

    private Task OnPageChangedAsync(int page) => CommitAsync(_filter with { Page = page }, replace: false);

    private Task ClearAsync() => CommitAsync(_filter.Cleared());

    private Task CommitAsync(KbListFilter filter, bool replace = true)
    {
        Navigation.NavigateTo(filter.Uri(), new NavigationOptions { ReplaceHistoryEntry = replace });
        return Task.CompletedTask;
    }

    private static Guid? ParseGuid(ChangeEventArgs e) => Guid.TryParse(e.Value as string, out var id) ? id : null;

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
