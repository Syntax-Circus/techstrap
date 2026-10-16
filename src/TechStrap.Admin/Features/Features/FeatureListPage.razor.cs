using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Features;

public partial class FeatureListPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<ProductDto> _products = [];
    private PagedResponse<FeatureSummaryDto>? _result;
    private string _search = "", _product = "", _status = "";
    private int _page = 1, _loadId;
    private bool _loading;
    private string? _error;
    [SupplyParameterFromQuery(Name = "product")] public Guid? Product { get; set; }
    [SupplyParameterFromQuery(Name = "q")] public string? Search { get; set; }
    [SupplyParameterFromQuery(Name = "status")] public string? Status { get; set; }
    [SupplyParameterFromQuery(Name = "page")] public int? PageNumber { get; set; }
    protected override async Task OnInitializedAsync()
    {
        var result = await Products.ListAsync(_lifetime.Token);
        if (result.IsSuccess) _products = result.Value;
    }
    protected override async Task OnParametersSetAsync()
    {
        _product = Product?.ToString() ?? "";
        _search = Search ?? "";
        _status = FeatureProgressStatuses.All.Contains(Status ?? "") ? Status! : "";
        _page = Math.Max(1, PageNumber ?? 1);
        await LoadAsync();
    }
    private void SearchAsync() => Navigate(1);
    private void ChangePageAsync(int page) => Navigate(page);
    private void Navigate(int page) => Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(new Dictionary<string, object?> { ["q"] = string.IsNullOrWhiteSpace(_search) ? null : _search, ["product"] = Guid.TryParse(_product, out var id) ? id : null, ["status"] = _status.Length == 0 ? null : _status, ["page"] = page == 1 ? null : page }));
    private async Task LoadAsync()
    {
        var loadId = ++_loadId;
        _loading = true;
        _error = null;
        try
        {
            var result = await Features.ListAsync(Guid.TryParse(_product, out var id) ? id : null, _search, _status.Length == 0 ? null : _status, _page, _lifetime.Token);
            if (loadId != _loadId) return;
            if (result.IsSuccess) _result = result.Value;
            else _error = "The features could not be loaded. Refresh to try again.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (loadId == _loadId) _loading = false; }
    }
    public void Dispose() { _loadId++; _lifetime.Cancel(); _lifetime.Dispose(); }
}
