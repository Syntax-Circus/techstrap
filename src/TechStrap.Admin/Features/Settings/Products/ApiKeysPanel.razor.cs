using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The keys of one product: the list, the create form and the revoke confirmation. Creating a key hands its plaintext straight to <see cref="NewApiKeyDialog"/> and keeps nothing; the list shows the label,
/// the kind as a badge (the word, plus a border style) and the prefix only. Revoke is a medium-tier confirmation that says apps using the key will stop working. Writes use
/// <see cref="CancellationToken.None"/> and are never retried: an unknown outcome says so and offers a reload, never a bare retry, and a lost create answer says its secret cannot be shown.
/// </summary>
public sealed partial class ApiKeysPanel : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private NewApiKeyDialog? _dialog;
    private IReadOnlyList<ApiKeyRowViewModel> _rows = [];
    private ApiKeyRowViewModel? _revoking;
    private string _kind = string.Empty;
    private string _label = string.Empty;
    private string? _loadError;
    private string? _formError;
    private string? _kindError;
    private string? _labelError;
    private string? _revokeError;
    private bool _loading = true;
    private bool _busy;
    private bool _createUncertain;
    private bool _revokeBusy;
    private bool _revokeUncertain;
    private bool _disposed;
    private Guid _loadedFor;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    [Parameter, EditorRequired]
    public string ProductName { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor != ProductId)
        {
            _loadedFor = ProductId;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            var result = await Products.ListApiKeysAsync(ProductId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ApiKeyRowViewModel.From).OrderByDescending(r => r.CreatedAt)];
            }
            else
            {
                _loadError = $"{ApiKeysCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        _createUncertain = false;
        await LoadAsync();
    }

    private void OnKindChanged(ChangeEventArgs e)
    {
        _kind = e.Value?.ToString() ?? string.Empty;
        _kindError = null;
    }

    private void OnLabelInput(ChangeEventArgs e)
    {
        _label = e.Value?.ToString() ?? string.Empty;
        _labelError = null;
    }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }

        _formError = null;
        _kindError = null;
        _labelError = null;
        _createUncertain = false;
        if (_kind.Length == 0)
        {
            _kindError = ApiKeysCopy.KindRequired;
            return;
        }

        if (_label.Trim().Length > 100)
        {
            _labelError = ApiKeysCopy.LabelTooLong;
            return;
        }

        _busy = true;
        try
        {
            var label = string.IsNullOrWhiteSpace(_label) ? null : _label.Trim();
            var result = await Products.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest(_kind, label), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowCreateFailure(result.Errors);
                return;
            }

            // The plaintext goes straight into the dialog and nowhere else: not into the rows, the status bar, a field of this component or a URL.
            var created = result.Value;
            _rows = [ApiKeyRowViewModel.From(created.Key), .. _rows];
            _label = string.Empty;
            StatusMessages.Show(ApiKeysCopy.CreatedMessage(created.Key.Kind, ProductName));
            _dialog!.Show(created.Key, created.PlaintextKey);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowCreateFailure(IReadOnlyList<ResultError> errors)
    {
        var first = errors[0];
        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _createUncertain = true;
        }
        else if (first.Code == ApiErrorCodes.ProductNotFound)
        {
            _formError = ApiKeysCopy.ProductGone;
        }
        else if (first.Code == ApiErrorCodes.ApiKeyKindInvalid || first.Target == ApiFields.Kind)
        {
            _kindError = first.Message;
        }
        else if (first.Target == ApiFields.Label)
        {
            _labelError = first.Message;
        }
        else
        {
            _formError = first.Message;
        }
    }

    private void AskRevoke(ApiKeyRowViewModel row)
    {
        _revokeError = null;
        _revokeUncertain = false;
        _revoking = row;
    }

    private void CancelRevoke()
    {
        if (!_revokeBusy)
        {
            _revoking = null;
            _revokeError = null;
            _revokeUncertain = false;
        }
    }

    private async Task ConfirmRevokeAsync()
    {
        if (_revokeBusy || _revoking is not { } key)
        {
            return;
        }

        _revokeBusy = true;
        _revokeError = null;
        _revokeUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Products.RevokeApiKeyAsync(ProductId, key.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Select(r => r.Id == key.Id ? r with { RevokedAt = Time.GetUtcNow() } : r)];
                _revoking = null;
                StatusMessages.Show(ApiKeysCopy.RevokedMessage(key.Name));
                return;
            }

            await ShowRevokeFailureAsync(result.Errors[0]);
        }
        finally
        {
            _revokeBusy = false;
        }
    }

    private async Task ShowRevokeFailureAsync(ResultError error)
    {
        if (error.Code == ApiErrorCodes.ApiKeyNotFound)
        {
            _revoking = null;
            StatusMessages.Show(ApiKeysCopy.KeyGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            // The revoke may have been applied before the answer was lost: never a bare "try again".
            _revokeError = ApiKeysCopy.RevokeUncertain;
            _revokeUncertain = true;
        }
        else
        {
            _revokeError = ApiKeysCopy.RevokeFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _revoking = null;
        _revokeError = null;
        _revokeUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
