using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The command palette (UX-BRIEF-admin): a modal combobox on the native <c>dialog</c> element, opened with Ctrl+K or Cmd+K. Typing filters the commands; ArrowDown, ArrowUp, Home and End
/// move the selection (<c>palette.js</c> takes those keys from the browser); Enter or a click runs the selected command; Esc closes it and the browser returns focus to where it was.
/// Three rules decide what runs. The list is <see cref="CommandRegistry.Available"/> for this session, so an agent who is not an Admin never sees an admin command, and the same check
/// runs again when a command is chosen, because the session can change while the palette is open. The palette closes first and the command runs after the dialog has closed in the browser,
/// so a command that moves focus finds the page and not the inert background. And a command runs once: a second Enter or click while one is pending or running does nothing.
/// The owner controls <see cref="Open"/>. Nothing here may end the circuit: a script failure leaves the palette unopened and a command that throws is logged by type and reported in the status bar.
/// </summary>
public sealed partial class CommandPalette : IAsyncDisposable
{
    private const string DialogModule = "./js/dialog.js";
    private const string PaletteModule = "./js/palette.js";
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private ElementReference _dialog;
    private ElementReference _input;
    private ElementReference _list;
    private IJSObjectReference? _dialogScript;
    private IJSObjectReference? _paletteScript;
    private DotNetObjectReference<CommandPalette>? _self;
    private PaletteCommand? _pending;
    private bool _running;
    private bool _shown;
    private bool _openSeen;
    private bool _reveal;
    private int _index;
    private string _query = string.Empty;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Inject]
    private CommandRegistry Registry { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ILogger<CommandPalette> Logger { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; }

    /// <summary>Raised when the palette should close: Esc, a stray close by the browser, or a command was chosen.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    private string InputId => $"ts-palette-input-{_id}";

    private string ListId => $"ts-palette-list-{_id}";

    private string OptionId(int index) => $"ts-palette-option-{_id}-{index}";

    private string? ActiveOptionId => Matches.Count == 0 ? null : OptionId(SelectedIndex);

    /// <summary>The selected line, kept inside the list even when the list got shorter since it was chosen.</summary>
    private int SelectedIndex => Math.Clamp(_index, 0, Math.Max(Matches.Count - 1, 0));

    /// <summary>What the agent may see for what they typed. Nothing is listed while the palette is closed.</summary>
    private IReadOnlyList<PaletteCommand> Matches => Open ? CommandRegistry.Filter(Registry.Available(Session.IsAdmin), _query) : [];

    protected override void OnParametersSet()
    {
        if (Open && !_openSeen)
        {
            // A new cycle starts empty and on the first line, with nothing left over from a command that was pending when it closed.
            _query = string.Empty;
            _index = 0;
            _pending = null;
        }

        _openSeen = Open;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await SyncDialogAsync();

        if (Open)
        {
            if (_reveal)
            {
                _reveal = false;
                await TryAsync(async () => await _paletteScript!.InvokeVoidAsync("reveal", _list));
            }

            return;
        }

        await RunPendingAsync();
    }

    private async Task SyncDialogAsync()
    {
        if (Open == _shown)
        {
            return;
        }

        try
        {
            _dialogScript ??= await Js.InvokeAsync<IJSObjectReference>("import", DialogModule);
            if (Open)
            {
                _paletteScript ??= await Js.InvokeAsync<IJSObjectReference>("import", PaletteModule);
                _self ??= DotNetObjectReference.Create(this);
                await _paletteScript.InvokeVoidAsync("attach", _input, _self);
                await _dialogScript.InvokeVoidAsync("open", _dialog, _input, _self);
            }
            else
            {
                await _dialogScript.InvokeVoidAsync("close", _dialog);
            }

            // Only after the script succeeded: a failed open must not leave us believing the dialog is showing.
            _shown = Open;
        }
        catch (Exception ex) when (IsScriptFailure(ex))
        {
            // No script, no circuit: the palette stays shut. Log the type, never a message (a message could carry what the agent typed).
            Logger.LogWarning("The command palette could not {Step} the dialog ({ExceptionType}).", Open ? "open" : "close", ex.GetType().Name);
            _shown = false;
            if (Open)
            {
                await CloseAsync();
            }
        }
    }

    /// <summary>The moment after the dialog closed: the chosen command runs, once.</summary>
    private async Task RunPendingAsync()
    {
        if (_pending is not { } command || _running)
        {
            return;
        }

        _pending = null;
        if (Session.State != AgentSessionState.Ready)
        {
            // The session lapsed between the choice and the moment the dialog closed: nothing runs for a session the API no longer vouches for.
            return;
        }

        _running = true;
        try
        {
            await command.RunAsync();
        }
        catch (Exception ex)
        {
            // OnAfterRenderAsync must never throw: an exception here would end the circuit, whatever the exception is (a cancelled command included: nothing awaits a cancellation
            // from the palette). Log the type only, and tell the agent.
            Logger.LogWarning("A command palette command {CommandId} failed ({ExceptionType}).", command.Id, ex.GetType().Name);
            StatusMessages.Show(PaletteCopy.CommandFailed);
        }
        finally
        {
            _running = false;
        }
    }

    private void OnQuery(ChangeEventArgs e)
    {
        _query = e.Value as string ?? string.Empty;
        _index = 0;
        _reveal = true;
    }

    /// <summary>Called by <c>palette.js</c> for ArrowDown, ArrowUp, Home, End and Enter in the input.</summary>
    [JSInvokable]
    public Task NavigateKey(string key) => InvokeAsync(async () =>
    {
        var matches = Matches;
        if (key == "Enter")
        {
            if (matches.Count > 0)
            {
                await RunAsync(matches[SelectedIndex]);
            }

            return;
        }

        if (matches.Count == 0)
        {
            return;
        }

        var current = SelectedIndex;
        _index = key switch
        {
            "ArrowDown" => (current + 1) % matches.Count,
            "ArrowUp" => (current - 1 + matches.Count) % matches.Count,
            "Home" => 0,
            "End" => matches.Count - 1,
            _ => _index,
        };
        _reveal = true;
        StateHasChanged();
    });

    /// <summary>Called by <c>dialog.js</c> when the browser closed the dialog behind our back (a repeated Esc): that is a close.</summary>
    [JSInvokable]
    public Task NativeClosed() => InvokeAsync(async () =>
    {
        if (Open)
        {
            await CloseAsync();
            if (Open)
            {
                _shown = false;
                StateHasChanged();
            }
        }
    });

    /// <summary>Called by <c>dialog.js</c> for an Esc it stopped. The palette is never held open, so this never happens, but the handle must answer.</summary>
    [JSInvokable]
    public Task EscapePressed() => InvokeAsync(CloseAsync);

    private Task CloseAsync() => OnClose.InvokeAsync();

    /// <summary>
    /// Chooses a command: closes the palette now and lets <see cref="RunPendingAsync"/> run it once the dialog is gone. Does nothing while another choice is pending or running,
    /// nothing for an admin command when the session is not an admin's, and nothing at all once the session is no longer Ready (it lapsed while the palette was open).
    /// </summary>
    private async Task RunAsync(PaletteCommand command)
    {
        if (_pending is not null || _running || Session.State != AgentSessionState.Ready || (command.AdminOnly && !Session.IsAdmin))
        {
            return;
        }

        _pending = command;
        await CloseAsync();
    }

    private static bool IsScriptFailure(Exception ex) =>
        ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private static async Task TryAsync(Func<Task> script)
    {
        try
        {
            await script();
        }
        catch (Exception ex) when (IsScriptFailure(ex))
        {
            // A selection that is not scrolled into view is not worth an error.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        try
        {
            if (_paletteScript is not null)
            {
                await _paletteScript.InvokeVoidAsync("detach", _input);
                await _paletteScript.DisposeAsync();
            }

            if (_dialogScript is not null)
            {
                if (_shown)
                {
                    await _dialogScript.InvokeVoidAsync("close", _dialog);
                }

                await _dialogScript.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException)
        {
            // The circuit is already gone; the browser has dropped the dialog with the page.
        }
    }
}
