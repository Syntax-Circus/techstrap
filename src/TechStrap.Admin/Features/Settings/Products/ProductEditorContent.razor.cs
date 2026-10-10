using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// Creates a product or edits one (Admin only). Nothing on this page loses what the agent typed: a failed, refused or uncertain save leaves the form exactly as it was, and only a successful save
/// or an explicit Reload replaces the model. The update always carries the product's current <c>IsActive</c> (an omitted flag would deactivate it) and the <c>Version</c> it was loaded with, so a
/// stale save is a 409 and never an overwrite. Field errors from the API are mapped by their kebab-case target. Writes use <see cref="CancellationToken.None"/>: a save that is on its way is never
/// abandoned because the agent left the page. The logo address is checked with <see cref="BrandingRules.IsAcceptableLogoUrl"/> before anything is sent, and the preview never loads an address that failed it.
/// </summary>
public sealed partial class ProductEditorContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _errors = [];
    private ProductEditorViewModel _model = new();
    private Guid _id;
    private string? _loadedFor;
    private string? _loadError;
    private string? _formError;
    private bool _creating;
    private bool _loading;
    private bool _busy;
    private bool _logoBusy;
    private bool _dirty;
    private bool _conflict;
    private bool _uncertain;
    private bool _gone;
    private bool _notFound;
    private bool _disposed;
    private int _loadId;
    private int _idEpoch;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    /// <summary>The route segment: a product id, or absent on <c>/settings/products/new</c>.</summary>
    [Parameter]
    public string? Id { get; set; }

    private string Title => _creating ? ProductsCopy.EditorNewTitle : string.IsNullOrWhiteSpace(_model.Name) ? ProductsCopy.Heading : _model.Name;

    // Save is offered for a new product, and for an edit once something changed. After a create whose outcome is unknown the product may exist: no second create (it would meet a 409 product-key-taken
    // that says the wrong thing) until the agent has left for the products list.
    // A logo upload or removal that is on its way holds the save, so the Version it returns is the one the save sends.
    private bool CanSave => (_creating ? !_uncertain : _dirty) && !_logoBusy;

    // An uploaded logo wins, as it does wherever the logo is shown. The preview never loads a typed address that failed the rule.
    private string? PreviewLogo => _model.UploadedLogoUrl
        ?? (BrandingRules.IsAcceptableLogoUrl(_model.LogoPath) && !string.IsNullOrWhiteSpace(_model.LogoPath) ? _model.LogoPath.Trim() : null);

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor == (Id ?? string.Empty))
        {
            return;
        }

        _loadedFor = Id ?? string.Empty;

        // A load or a save that is still running belongs to the previous product (or to the new-product form): it must change nothing here.
        _idEpoch++;
        _loadId++;
        _loading = false;
        _busy = false;
        _creating = Id is null;
        _conflict = false;
        _uncertain = false;
        _formError = null;
        _loadError = null;
        _gone = false;
        _notFound = false;
        _dirty = false;
        _errors.Clear();
        if (_creating)
        {
            _model = new ProductEditorViewModel();
            return;
        }

        if (!Guid.TryParse(Id, out _id))
        {
            _notFound = true;
            _model = new ProductEditorViewModel();
            Navigation.NotFound();
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        // Only the latest load may change the screen: a slow answer that was overtaken (a reload, another product) is ignored.
        var loadId = ++_loadId;
        _loading = true;
        _loadError = null;
        _gone = false;
        try
        {
            var result = await Products.GetAsync(_id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _model = ProductEditorViewModel.From(result.Value);
                _errors.Clear();
                _dirty = false;
            }
            else
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.ProductNotFound;
                _loadError = _gone ? ProductsCopy.EditorGone : $"{ProductsCopy.EditorLoadFailed} {result.Errors[0].Message}";
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

    private async Task ReloadAsync()
    {
        _conflict = false;
        _uncertain = false;
        _formError = null;
        await LoadAsync();
    }

    private void OnInput(Action<string> set, ChangeEventArgs e)
    {
        set(e.Value?.ToString() ?? string.Empty);
        _dirty = true;
    }

    private void OnActiveChanged(ChangeEventArgs e)
    {
        _model.IsActive = e.Value is true;
        _dirty = true;
    }

    private void OnListedChanged(ChangeEventArgs e)
    {
        _model.ListedOnLanding = e.Value is true;
        _dirty = true;
    }

    // Only the uploaded logo and the version are taken from the answer: a pending edit on screen is never replaced.
    private void OnLogoChanged(ProductDto saved)
    {
        _model.UploadedLogoUrl = saved.Branding.UploadedLogoUrl;
        _model.Version = saved.Version;
    }

    private void OnLogoUploadingChanged(bool uploading) => _logoBusy = uploading;

    private void CheckField(string field)
    {
        var message = _model.Check(field, _creating);
        if (message is null)
        {
            _errors.Remove(field);
        }
        else
        {
            _errors[field] = message;
        }
    }

    private bool CheckAll()
    {
        foreach (var field in ProductFields.All)
        {
            CheckField(field);
        }

        return _errors.Count == 0;
    }

    private async Task SaveAsync()
    {
        if (_busy || _logoBusy || (_creating && _uncertain))
        {
            return;
        }

        _formError = null;
        _conflict = false;
        _uncertain = false;
        if (!CheckAll())
        {
            return;
        }

        var epoch = _idEpoch;
        var creating = _creating;
        _busy = true;
        try
        {
            var result = _creating
                ? await Products.CreateAsync(_model.ToCreateRequest(), CancellationToken.None)
                : await Products.UpdateAsync(_id, _model.ToUpdateRequest(), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _idEpoch)
            {
                // The agent moved to another product (or the new-product form) while this was on its way. The write is done and is never abandoned, but the screen is not its any more: say so once, change nothing else.
                if (result.IsSuccess)
                {
                    StatusMessages.Show(creating ? ProductsCopy.Created(result.Value.Name) : ProductsCopy.Saved(result.Value.Name));
                }

                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors);
                return;
            }

            var saved = result.Value;
            if (_creating)
            {
                StatusMessages.Show(ProductsCopy.Created(saved.Name));
                Navigation.NavigateTo($"/settings/products/{saved.Id}/keys");
                return;
            }

            _model = ProductEditorViewModel.From(saved);
            _dirty = false;
            StatusMessages.Show(ProductsCopy.Saved(saved.Name));
        }
        finally
        {
            if (epoch == _idEpoch)
            {
                _busy = false;
            }
        }
    }

    private void ShowFailure(IReadOnlyList<ResultError> errors)
    {
        var first = errors[0];
        if (first.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _conflict = true;
        }
        else if (first.Code == ApiErrorCodes.ProductNotFound)
        {
            _formError = ProductsCopy.EditorGone;
        }
        else if (first.Code == ApiErrorCodes.ProductKeyTaken)
        {
            _formError = ProductsCopy.ProductKeyTaken;
        }
        else if (first.Code == ApiErrorCodes.ProductHostTaken)
        {
            _errors.TryAdd(ApiFields.PortalHost, ProductsCopy.PortalHostTaken);
        }
        else if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _uncertain = true;
        }
        else
        {
            MapFieldErrors(errors);
        }
    }

    // A 400 names the field in kebab-case. Anything the form has no field for is shown once, above the form, in the API's words.
    private void MapFieldErrors(IReadOnlyList<ResultError> errors)
    {
        foreach (var error in errors)
        {
            if (error.Target is { } target && ProductFields.All.Contains(target) && (_creating || !ProductFields.CreateOnly.Contains(target)))
            {
                _errors.TryAdd(target, error.Message);
            }
            else
            {
                _formError ??= error.Message;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
