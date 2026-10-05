using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>An uploaded picture: the address the API serves it from, and the alt text the editor puts in its Markdown.</summary>
public sealed record KbUploadedImage(string AltText, string Url);

/// <summary>
/// Picks one picture, checks its type and size before anything is sent, uploads it and raises <see cref="OnUploaded"/> once. A picture the browser or the API refuses never changes the article.
/// An upload has no effect on any article until the editor adds the returned address, so an answer that was lost needs no hold: the agent picks the picture again, and the earlier copy, if the API kept
/// one, is an unreferenced file (D-044: no orphan cleanup). The write is never cancelled by the screen closing (<see cref="CancellationToken.None"/>); a result that arrives after that is dropped.
/// </summary>
public sealed partial class KbImageUploadButton : IDisposable
{
    private readonly string _inputId = $"ts-kb-image-{Guid.NewGuid():N}"[..20];
    private string? _error;
    private bool _uploading;
    private bool _disposed;
    private int _inputKey;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private ILogger<KbImageUploadButton> Logger { get; set; } = default!;

    /// <summary>Disables the picker while the form it belongs to is busy.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Raised once per accepted picture, after the API stored it.</summary>
    [Parameter]
    public EventCallback<KbUploadedImage> OnUploaded { get; set; }

    private static string Accept => string.Join(',', KbDefaults.ImageExtensions);

    private async Task OnPickedAsync(InputFileChangeEventArgs e)
    {
        if (_uploading)
        {
            return;
        }

        _error = null;
        var file = e.File;
        if (!KbDefaults.ImageExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
        {
            _error = KbEditorCopy.ImageTypeNotAllowed;
            _inputKey++;
            return;
        }

        if (file.Size > KbLimits.MaxImageBytes)
        {
            _error = KbEditorCopy.ImageTooLarge(TicketDisplay.FileSize(KbLimits.MaxImageBytes));
            _inputKey++;
            return;
        }

        _uploading = true;
        StateHasChanged();
        try
        {
            // The browser's file is read here, before anything is sent, so a read that fails is told apart from an upload whose answer was lost.
            byte[] bytes;
            try
            {
                await using var source = file.OpenReadStream(KbLimits.MaxImageBytes, CancellationToken.None);
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy, CancellationToken.None);
                bytes = copy.ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only the type is logged, never the file name or the message.
                Logger.LogWarning("A picture could not be read, {ExceptionType}.", ex.GetType().Name);
                if (!_disposed)
                {
                    _error = KbEditorCopy.ImageReadFailed;
                }

                return;
            }

            var result = await Kb.UploadImageAsync(new KbImageFile(file.Name, file.ContentType, () => new MemoryStream(bytes, writable: false)), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                await OnUploaded.InvokeAsync(new KbUploadedImage(MarkdownSnippets.AltFromFileName(file.Name), result.Value.Url));
            }
            else
            {
                _error = ErrorFor(result.Errors[0]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A file that cannot be read (the browser dropped the handle) or any other fault: only the type is logged, never the file name.
            Logger.LogWarning("An image upload ended with an unmapped {ExceptionType}.", ex.GetType().Name);
            if (!_disposed)
            {
                _error = KbEditorCopy.ImageUncertain;
            }
        }
        finally
        {
            if (!_disposed)
            {
                _uploading = false;
                _inputKey++;
            }
        }
    }

    private static string ErrorFor(SyntaxCircus.Common.ResultError error) => error.Code switch
    {
        ApiErrorCodes.KbImageTypeNotAllowed => KbEditorCopy.ImageTypeNotAllowed,
        ApiErrorCodes.KbImageTooLarge or ApiErrorCodes.RequestTooLarge => KbEditorCopy.ImageTooLarge(TicketDisplay.FileSize(KbLimits.MaxImageBytes)),
        _ when ApiErrorCodes.IsUncertainWrite(error.Code) => KbEditorCopy.ImageUncertain,
        _ => $"{KbEditorCopy.ImageFailed} {error.Message}",
    };

    public void Dispose() => _disposed = true;
}
