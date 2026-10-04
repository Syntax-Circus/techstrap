using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// Shows a new API key once. The plaintext lives only in this component's field, from <see cref="Show"/> until the dialog closes; it is never logged, never put in a URL or an
/// attribute other than the read-only field's value, and the field is not rendered at all once the dialog is closed. Cancel and Esc do nothing but say what is missing until the agent ticks
/// "I have stored this key": the dialog cannot be dismissed by accident, and a dismissal after the tick clears the key. Copying goes to the clipboard through <c>clipboard.js</c>; when the
/// browser refuses (or the script cannot run) the text is selected so the agent can press Ctrl+C.
/// </summary>
public sealed partial class NewApiKeyDialog : IAsyncDisposable
{
    private const string ModulePath = "./js/clipboard.js";

    private ElementReference _secret;
    private IJSObjectReference? _module;
    private string? _plaintext;
    private string? _kind;
    private string? _hint;
    private string? _copyStatus;
    private bool _open;
    private bool _stored;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The product's name, for the sentence that says the key is shown only once.</summary>
    [Parameter]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Raised once, after the dialog has closed and the key has been cleared.</summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>Opens the dialog with a freshly created key. The caller does not keep the plaintext.</summary>
    public void Show(ProductApiKeyDto key, string plaintext)
    {
        _plaintext = plaintext;
        _kind = key.Kind;
        _stored = false;
        _hint = null;
        _copyStatus = null;
        _open = true;
        StateHasChanged();
    }

    private void OnStoredChanged(ChangeEventArgs e)
    {
        _stored = e.Value is true;
        if (_stored)
        {
            _hint = null;
        }
    }

    private async Task TryCloseAsync()
    {
        if (!_stored)
        {
            _hint = ApiKeysCopy.StoreFirst;
            return;
        }

        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (!_stored)
        {
            return;
        }

        Clear();
        await OnClosed.InvokeAsync();
    }

    private void Clear()
    {
        _plaintext = null;
        _kind = null;
        _hint = null;
        _copyStatus = null;
        _stored = false;
        _open = false;
    }

    private async Task CopyAsync()
    {
        if (_plaintext is null)
        {
            return;
        }

        var copied = false;
        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            copied = await _module.InvokeAsync<bool>("copyText", _plaintext);
            if (!copied)
            {
                await _module.InvokeVoidAsync("selectText", _secret);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // The script could not run: the key is still on screen in a read-only field the agent can select by hand.
        }

        _copyStatus = copied ? ApiKeysCopy.Copied : ApiKeysCopy.CopyFailed;
    }

    public async ValueTask DisposeAsync()
    {
        _plaintext = null;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
