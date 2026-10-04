using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

public enum ShortcutAction
{
    MoveDown,
    MoveUp,
    OpenSelected,
    FocusSearch,
    Reply,
    Note,
    FocusAssignee,
    NotSpam,
    Send,
    Escape,
    Help,
}

/// <summary>What <c>wwwroot/js/shortcuts.js</c> reports for one key press. The script decides <see cref="Typing"/> and <see cref="OnBody"/> from the focused element.</summary>
/// <param name="Key">The <c>KeyboardEvent.key</c> value.</param>
/// <param name="Typing">Focus is in a text field, select or editable area.</param>
/// <param name="OnBody">Nothing interactive has focus (so Enter and the arrows are free to act on the page).</param>
/// <param name="Scope">The nearest <c>data-shortcut-scope</c> of the focused element, if any.</param>
public sealed record KeyPress(string Key, bool Ctrl, bool Meta, bool Alt, bool Typing, bool OnBody, string? Scope);

/// <summary>
/// The keyboard layer (UX-BRIEF-admin, Density and keyboard shortcuts). The script reports key presses; this class decides whether one is a
/// shortcut and tells whoever subscribed. Pages and components subscribe to <see cref="Pressed"/> while they are on screen and ignore what is not theirs.
/// Single-key shortcuts are off while the user types, and when <see cref="SingleKeyEnabled"/> is false (WCAG 2.1.4). The command palette is deferred to PHASE-07c.
/// </summary>
public sealed class ShortcutService(IJSRuntime js) : IAsyncDisposable
{
    /// <summary>The <c>data-shortcut-scope</c> value of the reply composer; Ctrl+Enter sends only from inside it.</summary>
    public const string ComposerScope = "composer";

    private const string ModulePath = "./js/shortcuts.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<ShortcutService>? _self;
    private Task? _starting;

    /// <summary>Raised once per recognised shortcut; every handler is awaited in subscription order.</summary>
    public event Func<ShortcutAction, Task>? Pressed;

    /// <summary>The My settings toggle arrives in PHASE-07b; until then the layer is always on.</summary>
    public bool SingleKeyEnabled { get; set; } = true;

    /// <summary>The pure decision: which action, if any, a key press means.</summary>
    public static ShortcutAction? Map(KeyPress press, bool singleKeyEnabled)
    {
        if (press.Ctrl || press.Meta)
        {
            return press.Key == "Enter" && press.Scope == ComposerScope ? ShortcutAction.Send : null;
        }

        if (press.Alt)
        {
            return null;
        }

        if (press.Key == "Escape")
        {
            // While typing, the script only blurs the field (the text is kept); this is the "back" Escape.
            return press.Typing ? null : ShortcutAction.Escape;
        }

        if (press.Typing || !singleKeyEnabled)
        {
            return null;
        }

        var key = press.Key.Length == 1 ? press.Key.ToLowerInvariant() : press.Key;
        return key switch
        {
            "j" => ShortcutAction.MoveDown,
            "k" => ShortcutAction.MoveUp,
            "ArrowDown" when press.OnBody => ShortcutAction.MoveDown,
            "ArrowUp" when press.OnBody => ShortcutAction.MoveUp,
            "Enter" when press.OnBody => ShortcutAction.OpenSelected,
            "/" => ShortcutAction.FocusSearch,
            "r" => ShortcutAction.Reply,
            "n" => ShortcutAction.Note,
            "e" => ShortcutAction.FocusAssignee,
            "u" => ShortcutAction.NotSpam,
            "?" => ShortcutAction.Help,
            _ => null,
        };
    }

    /// <summary>
    /// Imports the module and registers the document listener. Call it once per circuit, after the first render. Concurrent calls share one start, and a
    /// circuit that is already gone is not an error. Any other failure lets the next call try again.
    /// </summary>
    public Task StartAsync() => _starting ??= StartCoreAsync();

    private async Task StartCoreAsync()
    {
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            var self = DotNetObjectReference.Create(this);
            _module = module;
            _self = self;
            await module.InvokeVoidAsync("register", self);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and so is the page that would have held the listener.
        }
        catch
        {
            _starting = null;
            throw;
        }
    }

    [JSInvokable]
    public async Task OnKeyAsync(KeyPress press)
    {
        var action = Map(press, SingleKeyEnabled);
        if (action is null || Pressed is null)
        {
            return;
        }

        foreach (var handler in Pressed.GetInvocationList().Cast<Func<ShortcutAction, Task>>())
        {
            await handler(action.Value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("unregister");
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and so is the page that held the listener.
        }
        finally
        {
            _self?.Dispose();
        }
    }
}
