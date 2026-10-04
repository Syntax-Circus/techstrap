using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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

        Shortcuts = JSInterop.SetupModule("./js/shortcuts.js");
        Shortcuts.SetupVoid("register", _ => true).SetVoidResult();
        Shortcuts.SetupVoid("unregister", _ => true).SetVoidResult();

        Dialogs = JSInterop.SetupModule("./js/dialog.js");
        Dialogs.SetupVoid("open", _ => true).SetVoidResult();
        Dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    protected FakeTimeProvider Time { get; }

    /// <summary>The <c>shortcuts.js</c> module double; use <c>VerifyInvoke("register")</c>.</summary>
    protected BunitJSModuleInterop Shortcuts { get; }

    /// <summary>The <c>dialog.js</c> module double; use <c>VerifyInvoke("open")</c> and read the arguments of the invocation.</summary>
    protected BunitJSModuleInterop Dialogs { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();

    /// <summary>Simulates the page script reporting a key press (what <c>shortcuts.js</c> sends over the JS bridge).</summary>
    protected Task PressAsync(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null) =>
        Renderer.Dispatcher.InvokeAsync(() => ShortcutService.OnKeyAsync(new KeyPress(key, ctrl, Meta: false, Alt: false, typing, onBody, scope)));
}
