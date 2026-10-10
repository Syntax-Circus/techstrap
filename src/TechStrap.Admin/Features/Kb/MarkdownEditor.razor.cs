using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// A plain textarea with a toolbar and a live preview beside it (PHASE-08: no editor library and no JavaScript). The text lives in the owner: this component reports every change through
/// <see cref="ValueChanged"/> and never keeps text of its own. Whenever <see cref="Value"/> changes, the preview waits <see cref="KbDefaults.PreviewDebounce"/> after the last change and then asks the API
/// to render it (<see cref="IKbClient.PreviewAsync"/>), so a burst of typing is one call. A call that a newer change has replaced is canceled, and an answer that arrives after a newer call started
/// is ignored (<c>_previewId</c>). A failed preview leaves the text and the last good preview alone and says so beside it. Nothing is previewed for an empty text. The timer and the call are
/// released when the component goes.
/// </summary>
public sealed partial class MarkdownEditor : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _call;
    private ITimer? _timer;
    private string? _scheduled;
    private string _html = string.Empty;
    private bool _previewing;
    private string? _previewError;
    private bool _showPreview;
    private bool _disposed;
    private int _previewId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private ILogger<MarkdownEditor> Logger { get; set; } = default!;

    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The id of the textarea, so the page's label, error and toolbar controls name it.</summary>
    [Parameter, EditorRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Disables the text and the toolbar while the form around it is busy.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The field's error, shown under the text.</summary>
    [Parameter]
    public string? ErrorMessage { get; set; }

    /// <summary>Raised when the text area loses focus, so the owner can check the field.</summary>
    [Parameter]
    public EventCallback<FocusEventArgs> OnBlur { get; set; }

    /// <summary>Raised with true when the API refused the text as too complex to render (kb-body-too-complex) and with false when a preview succeeds, so the owner can show the body's field error.</summary>
    [Parameter]
    public EventCallback<bool> OnTooComplex { get; set; }

    /// <summary>Raised with true while a picture is being uploaded and with false when that ends, so the owner can hold Save, Publish and Archive meanwhile.</summary>
    [Parameter]
    public EventCallback<bool> OnUploadingChanged { get; set; }

    /// <summary>What the text belongs to (the article shown). A picture that finishes after this changed is dropped, so it never lands in another article.</summary>
    [Parameter]
    public int UploadScope { get; set; }

    protected override void OnParametersSet()
    {
        // The text can change from outside (a reload, an image added) as well as by typing; both arrive here, so this is the one place the preview is scheduled.
        if (Value == _scheduled)
        {
            return;
        }

        _scheduled = Value;
        _call?.Cancel();
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(RefreshPreviewAsync), null, KbDefaults.PreviewDebounce, Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshPreviewAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return;
        }

        var id = ++_previewId;
        var text = Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            _call?.Cancel();
            _html = string.Empty;
            _previewing = false;
            _previewError = null;
            StateHasChanged();
            await OnTooComplex.InvokeAsync(false);
            return;
        }

        if (text.Length > KbEditorLimits.BodyMaxLength)
        {
            // The API refuses a text it could not save either (400 body-too-long), so it is not asked on every keystroke; the last good preview stays.
            _call?.Cancel();
            _previewing = false;
            _previewError = KbEditorCopy.PreviewTooLong;
            StateHasChanged();
            await OnTooComplex.InvokeAsync(false);
            return;
        }

        _call?.Cancel();
        _call?.Dispose();
        var call = _call = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previewing = true;
        StateHasChanged();
        try
        {
            var result = await Kb.PreviewAsync(new KbPreviewRequest(text), call.Token);
            if (_disposed || id != _previewId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _html = result.Value.Html;
                _previewError = null;
                await OnTooComplex.InvokeAsync(false);
            }
            else if (result.Errors[0].Code == ApiErrorCodes.KbBodyTooComplex)
            {
                // A calm, fixed message: the text is kept, the last good preview stays, and the owner is told so it can mark the body field. Never the API's words.
                _previewError = KbEditorCopy.PreviewTooComplex;
                await OnTooComplex.InvokeAsync(true);
            }
            else
            {
                _previewError = KbEditorCopy.PreviewFailed;
            }
        }
        catch (OperationCanceledException) when (call.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (id == _previewId && !_disposed)
        {
            // Only the type is logged: an exception message can carry text from the article.
            Logger.LogWarning("A preview ended with an unmapped {ExceptionType}.", ex.GetType().Name);
            _previewError = KbEditorCopy.PreviewFailed;
        }
        finally
        {
            if (!_disposed && id == _previewId)
            {
                _previewing = false;
                StateHasChanged();
            }
        }
    }

    private Task OnInputAsync(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value as string ?? string.Empty);

    private Task Add(string snippet, bool inline) => ValueChanged.InvokeAsync(MarkdownSnippets.Append(Value, snippet, inline));

    private Task OnImageUploadedAsync(KbUploadedImage image) => ValueChanged.InvokeAsync(MarkdownSnippets.Append(Value, MarkdownSnippets.Image(image.AltText, image.Url), inline: false));

    private void ShowPreview(bool show) => _showPreview = show;

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _call?.Dispose();
    }
}
