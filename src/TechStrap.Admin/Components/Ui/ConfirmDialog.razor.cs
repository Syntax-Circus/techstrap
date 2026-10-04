using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A modal confirmation built on the native <c>dialog</c> element (the browser supplies the focus trap and the inert background).
/// Enter never confirms: there is no form, both buttons are <c>type="button"</c>, and initial focus goes to the typed-confirmation input
/// or, without one, to the heading (never to a button). Esc cancels, except while a request is running. The owner controls <see cref="Open"/>.
/// </summary>
public partial class ConfirmDialog : IAsyncDisposable
{
    private const string ModulePath = "./js/dialog.js";
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private ElementReference _dialog;
    private ElementReference _title;
    private ElementReference _input;
    private IJSObjectReference? _module;
    private bool _shown;
    private bool _openSeen;
    private string _typed = string.Empty;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string ConfirmLabel { get; set; } = "Confirm";

    [Parameter]
    public string CancelLabel { get; set; } = ShellCopy.Cancel;

    /// <summary>An information dialog (the shortcut list): no confirm button, and the one remaining button, labelled by <see cref="CancelLabel"/>, closes it.</summary>
    [Parameter]
    public bool Informational { get; set; }

    /// <summary>Styles the confirm button as destructive (a danger style plus the word in the label, never colour alone).</summary>
    [Parameter]
    public bool Danger { get; set; }

    /// <summary>When set, Confirm stays disabled until the user types exactly this text (compared ignoring case and surrounding spaces).</summary>
    [Parameter]
    public string? RequiredText { get; set; }

    /// <summary>A request is running: both buttons, the input and Esc are inert.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>The failure from the last confirm. The dialog stays open and nothing else changes.</summary>
    [Parameter]
    public string? Error { get; set; }

    [Parameter]
    public EventCallback OnConfirm { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private string TitleId => $"ts-dialog-title-{_id}";

    private string BodyId => $"ts-dialog-body-{_id}";

    private string InputId => $"ts-dialog-input-{_id}";

    private string CssClass => Danger ? "ts-dialog ts-dialog--danger" : "ts-dialog";

    private string ConfirmCss => Danger ? "btn btn-danger" : "btn btn-primary";

    private string ConfirmPrompt => $"Type {RequiredText} to confirm";

    private bool CanConfirm => !Busy && (RequiredText is null || string.Equals(_typed.Trim(), RequiredText, StringComparison.OrdinalIgnoreCase));

    protected override void OnParametersSet()
    {
        if (Open && !_openSeen)
        {
            _typed = string.Empty;
        }

        _openSeen = Open;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var wantOpen = Open;
        if (wantOpen == _shown)
        {
            return;
        }

        _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        if (wantOpen)
        {
            await _module.InvokeVoidAsync("open", _dialog, RequiredText is null ? _title : _input);
        }
        else
        {
            await _module.InvokeVoidAsync("close", _dialog);
        }

        // Only after the script succeeded: a failed open must not leave us believing the dialog is showing.
        _shown = wantOpen;
    }

    private void OnTyped(ChangeEventArgs e) => _typed = e.Value as string ?? string.Empty;

    private Task ConfirmAsync() => CanConfirm ? OnConfirm.InvokeAsync() : Task.CompletedTask;

    private Task CancelAsync() => Busy ? Task.CompletedTask : OnCancel.InvokeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            if (_shown)
            {
                await _module.InvokeVoidAsync("close", _dialog);
            }

            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone; the browser has dropped the dialog with the page.
        }
    }
}
