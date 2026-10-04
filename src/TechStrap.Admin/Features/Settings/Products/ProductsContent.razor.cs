using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The product list (Admin only). The page makes no API call unless the session is an Admin: a plain agent gets the no-access page from <c>AdminOnly</c>, and the load below is not even attempted,
/// so no request is made that the API would refuse with a 403. The API returns every product to an Admin, inactive ones included.
/// </summary>
public sealed partial class ProductsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<ProductRowViewModel> _rows = [];
    private string? _error;
    private bool _loading = true;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Products.ListAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ProductRowViewModel.From).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal)];
            }
            else
            {
                _error = $"{ProductsCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
