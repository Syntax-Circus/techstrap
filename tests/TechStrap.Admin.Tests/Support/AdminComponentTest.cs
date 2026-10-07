using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The common setup of the Admin component tests: the shell services (message slot, shortcut service), a fake clock, and the two script modules
/// the shell imports, set up in bUnit's strict JS mode so an unexpected JS call fails the test instead of passing silently.
/// </summary>
public abstract class AdminComponentTest : BunitContext
{
    protected AdminComponentTest()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        Services.AddShell();
        Services.AddSingleton<TimeProvider>(Time);

        // Every component that draws live state asks for the client; this one never touches a hub. A test raises its events and reads what the components called.
        // The signed-in agent (Sam) every live component compares a change's actor with; a test that needs another session registers its own after this.
        Services.AddSingleton(_ => AgentSessions.SignedIn());
        LiveClient = new FakeTicketLiveClient();
        Services.AddSingleton<ITicketLiveClient>(LiveClient);

        Shortcuts = JSInterop.SetupModule("./js/shortcuts.js");
        Shortcuts.SetupVoid("register", _ => true).SetVoidResult();
        Shortcuts.SetupVoid("unregister", _ => true).SetVoidResult();

        Dialogs = JSInterop.SetupModule("./js/dialog.js");
        Dialogs.SetupVoid("open", _ => true).SetVoidResult();
        Dialogs.SetupVoid("close", _ => true).SetVoidResult();

        // MainLayout loads the stored preferences on its first render; by default nothing is stored (shortcuts on, theme auto).
        Preferences = JSInterop.SetupModule("./js/preferences.js");
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Preferences.Setup<bool>("save", _ => true).SetResult(true);

        // MainLayout also asks the browser for its time zone on the first render; by default it answers UTC. A test sets it up again to try another zone.
        Tz = JSInterop.SetupModule("./js/tz.js");
        Tz.Setup<string?>("zone", _ => true).SetResult("UTC");

        // The command palette (opened by MainLayout on Ctrl+K) and the ticket actions menu each import a small module the first time they are used.
        Palette = JSInterop.SetupModule("./js/palette.js");
        Palette.SetupVoid("attach", _ => true).SetVoidResult();
        Palette.SetupVoid("detach", _ => true).SetVoidResult();
        Palette.SetupVoid("reveal", _ => true).SetVoidResult();
        Menu = JSInterop.SetupModule("./js/menu.js");
        Menu.SetupVoid("attach", _ => true).SetVoidResult();
        Menu.SetupVoid("focusFirst", _ => true).SetVoidResult();
        Menu.SetupVoid("focusIfWithin", _ => true).SetVoidResult();
    }

    protected FakeTimeProvider Time { get; }

    /// <summary>The live client every component under test receives (<see cref="ITicketLiveClient"/>): raise changes and presence on it, read the joins and the composing calls.</summary>
    protected FakeTicketLiveClient LiveClient { get; }

    /// <summary>The <c>shortcuts.js</c> module double; use <c>VerifyInvoke("register")</c>.</summary>
    protected BunitJSModuleInterop Shortcuts { get; }

    /// <summary>The <c>dialog.js</c> module double; use <c>VerifyInvoke("open")</c> and read the arguments of the invocation.</summary>
    protected BunitJSModuleInterop Dialogs { get; }

    /// <summary>The <c>preferences.js</c> module double: <c>load</c> answers the defaults and <c>save</c> succeeds; read the arguments from <c>Invocations["save"]</c>.</summary>
    protected BunitJSModuleInterop Preferences { get; }

    /// <summary>The <c>tz.js</c> module double: <c>zone</c> answers "UTC"; set it up again to answer another zone or to fail.</summary>
    protected BunitJSModuleInterop Tz { get; }

    /// <summary>The <c>palette.js</c> module double: <c>attach</c>, <c>detach</c> and <c>reveal</c> succeed.</summary>
    protected BunitJSModuleInterop Palette { get; }

    /// <summary>The <c>menu.js</c> module double: <c>attach</c>, <c>focusFirst</c> and <c>focusIfWithin</c> succeed.</summary>
    protected BunitJSModuleInterop Menu { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();

    /// <summary>Simulates the page script reporting a key press (what <c>shortcuts.js</c> sends over the JS bridge).</summary>
    protected Task PressAsync(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null) =>
        Renderer.Dispatcher.InvokeAsync(() => ShortcutService.OnKeyAsync(new KeyPress(key, ctrl, Meta: false, Alt: false, typing, onBody, scope)));
}
