using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The API keys of one product (Admin only). Like every settings page it makes no call unless the session is an Admin.</summary>
public sealed partial class ProductKeysContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private ProductDto? _product;
    private string? _error;
    private bool _loading = true;
    private bool _gone;
    private Guid _loadedFor;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Parameter]
    public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor != Id)
        {
            _loadedFor = Id;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        _gone = false;
        try
        {
            var result = await Products.GetAsync(Id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _product = result.Value;
            }
            else
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.ProductNotFound;
                _error = _gone ? ProductsCopy.EditorGone : $"{ProductsCopy.EditorLoadFailed} {result.Errors[0].Message}";
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
