using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// Picks one logo, checks its type and size before anything is sent, uploads it and raises <see cref="OnChanged"/> with the re-read product; the remove button does the same for a removal.
/// The owner takes only the logo address and the version from the product it receives, so a pending edit is never lost. An image the browser or the API refuses changes nothing. Writes use
/// <see cref="CancellationToken.None"/>: one that is on its way is not abandoned because the screen closed, and a result that arrives after that is dropped.
/// </summary>
public sealed partial class ProductLogoUploadButton : IDisposable
{
    private string? _error;
    private bool _working;
    private bool _uploading;
    private bool _disposed;
    private int _inputKey;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private ILogger<ProductLogoUploadButton> Logger { get; set; } = default!;

    /// <summary>The saved product the logo belongs to.</summary>
    [Parameter]
    public Guid ProductId { get; set; }

    /// <summary>Whether the product has an uploaded logo, which is when the remove button is offered.</summary>
    [Parameter]
    public bool HasUploadedLogo { get; set; }

    /// <summary>Disables the picker and the remove button while the form it belongs to is busy.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Raised with the re-read product after an upload or a removal the API accepted.</summary>
    [Parameter]
    public EventCallback<ProductDto> OnChanged { get; set; }

    /// <summary>Raised with true when an upload or removal starts and with false when it ends, so the owner can hold its own save while a logo change is on its way.</summary>
    [Parameter]
    public EventCallback<bool> OnUploadingChanged { get; set; }

    private static string Accept => string.Join(',', ProductLogoLimits.AllowedExtensions);

    private async Task OnPickedAsync(InputFileChangeEventArgs e)
    {
        if (_working)
        {
            return;
        }

        _error = null;
        var file = e.File;
        if (!ProductLogoLimits.AllowedExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
        {
            _error = ProductsCopy.LogoTypeNotAllowed;
            _inputKey++;
            return;
        }

        if (file.Size > ProductLogoLimits.MaxBytes)
        {
            _error = ProductsCopy.LogoTooLarge;
            _inputKey++;
            return;
        }

        _uploading = true;
        await RunAsync(async () =>
        {
            // The browser's file is read here, before anything is sent, so a read that fails is told apart from an upload whose answer was lost.
            byte[] bytes;
            try
            {
                await using var source = file.OpenReadStream(ProductLogoLimits.MaxBytes, CancellationToken.None);
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy, CancellationToken.None);
                bytes = copy.ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only the type is logged, never the file name or the message.
                Logger.LogWarning("A logo could not be read, {ExceptionType}.", ex.GetType().Name);
                if (!_disposed)
                {
                    _error = ProductsCopy.LogoReadFailed;
                }

                return null;
            }

            return await Products.UploadLogoAsync(ProductId, new ProductLogoFile(file.Name, file.ContentType, () => new MemoryStream(bytes, writable: false)), CancellationToken.None);
        });
    }

    private async Task RemoveAsync()
    {
        if (_working)
        {
            return;
        }

        _error = null;
        _uploading = false;
        await RunAsync(() => Products.RemoveLogoAsync(ProductId, CancellationToken.None)!);
    }

    // One write at a time: starts the hold, runs the call, raises OnChanged for a success and maps a failure to a sentence. A null result means the call was never made (the file could not be read).
    private async Task RunAsync(Func<Task<Result<ProductDto>?>> call)
    {
        _working = true;
        StateHasChanged();
        await OnUploadingChanged.InvokeAsync(true);
        try
        {
            var result = await call();
            if (_disposed || result is null)
            {
                return;
            }

            if (result.IsSuccess)
            {
                await OnChanged.InvokeAsync(result.Value);
            }
            else
            {
                _error = ErrorFor(result.Errors[0]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any fault: only the type is logged, never the file name or the message.
            Logger.LogWarning("A logo change ended with an unmapped {ExceptionType}.", ex.GetType().Name);
            if (!_disposed)
            {
                _error = ProductsCopy.LogoUncertain;
            }
        }
        finally
        {
            if (!_disposed)
            {
                _working = false;
                _uploading = false;
                _inputKey++;
                await OnUploadingChanged.InvokeAsync(false);
            }
        }
    }

    private static string ErrorFor(ResultError error) => error.Code switch
    {
        ApiErrorCodes.ProductLogoTypeNotAllowed => ProductsCopy.LogoTypeNotAllowed,
        ApiErrorCodes.ProductLogoTooLarge or ApiErrorCodes.RequestTooLarge => ProductsCopy.LogoTooLarge,
        _ when ApiErrorCodes.IsUncertainWrite(error.Code) => ProductsCopy.LogoUncertain,
        _ => $"{ProductsCopy.LogoFailed} {error.Message}",
    };

    public void Dispose() => _disposed = true;
}
