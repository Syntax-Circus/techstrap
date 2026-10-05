using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using NSubstitute;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The rail and the layout sit outside every error boundary, so an exception out of their <c>OnAfterRenderAsync</c> would end the circuit. Each must log
/// the exception type (never the message, which can carry an address) and degrade.
/// </summary>
public sealed class LayoutResilienceTests : BunitContext
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private const string Secret = "secret-detail ada@example.com";

    private readonly RecordingLoggerProvider _logs = new();

    public LayoutResilienceTests()
    {
        Services.AddLogging(logging => logging.AddProvider(_logs));
        Services.AddShell();
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    }

    private void NoLeak() =>
        _logs.Lines.ShouldNotContain(line => line.Contains("secret-detail", StringComparison.Ordinal) || line.Contains("ada@example.com", StringComparison.Ordinal));

    [Fact]
    public async Task A_failing_badge_read_leaves_the_rail_working_and_logs_only_the_type()
    {
        this.AddAgentShell(AgentRoles.Admin);
        var deadLetters = Services.GetRequiredService<IDeadLettersClient>();
        deadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<SyntaxCircus.Common.Result<int>>(new InvalidOperationException(Secret)));
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal)));
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        NoLeak();
        await deadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    private void SetupScripts(Action<BunitJSModuleInterop> shortcuts)
    {
        var shortcutModule = JSInterop.SetupModule("./js/shortcuts.js");
        shortcuts(shortcutModule);
        shortcutModule.SetupVoid("unregister", _ => true).SetVoidResult();
        JSInterop.SetupModule("./js/dialog.js");
        var preferences = JSInterop.SetupModule("./js/preferences.js");
        preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        JSInterop.SetupModule("./js/tz.js").Setup<string?>("zone", _ => true).SetResult("UTC");
    }

    [Fact]
    public void A_shortcut_listener_that_cannot_start_leaves_the_pages_working_and_logs_only_the_type()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetException(new JSException(Secret)));

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("JSException", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find(".ts-brand").ShouldNotBeNull();
        NoLeak();
    }

    [Fact]
    public void A_shortcut_listener_that_is_cancelled_while_starting_leaves_the_pages_working_and_logs_only_the_type()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetException(new TaskCanceledException(Secret)));

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("TaskCanceledException", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        NoLeak();
    }

    [Fact]
    public void A_failing_preference_read_does_not_stop_the_time_zone_and_logs_only_the_type()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetVoidResult());
        var preferences = JSInterop.SetupModule("./js/preferences.js");
        preferences.Setup<StoredPreferences>("load", _ => true).SetException(new NotSupportedException(Secret));
        JSInterop.SetupModule("./js/tz.js").Setup<string?>("zone", _ => true).SetResult("Europe/London");

        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        Services.GetRequiredService<LocalTimeService>().Zone.Id.ShouldBe("Europe/London");
        NoLeak();
    }

    [Fact]
    public void A_shortcut_listener_that_cannot_start_does_not_stop_the_time_zone_or_blame_the_zone()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetException(new JSException(Secret)));
        JSInterop.SetupModule("./js/tz.js").Setup<string?>("zone", _ => true).SetResult("Europe/London");

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => Services.GetRequiredService<LocalTimeService>().Zone.Id.ShouldBe("Europe/London"));
        NoLeak();
    }

    [Fact]
    public void A_zone_that_cannot_be_read_does_not_say_the_shortcuts_failed()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetVoidResult());
        JSInterop.SetupModule("./js/tz.js").Setup<string?>("zone", _ => true).SetException(new NotSupportedException(Secret));

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("NotSupportedException", StringComparison.Ordinal)));
        _logs.Lines.ShouldNotContain(line => line.Contains("shortcuts could not be started", StringComparison.Ordinal));
        cut.Find("main.ts-main #page").TextContent.ShouldBe("page");
        NoLeak();
    }

    [Fact]
    public async Task The_rail_and_an_admin_page_stay_complete_when_the_session_expires_while_working()
    {
        this.AddAgentShell(AgentRoles.Admin);
        var session = Services.GetRequiredService<AgentSession>();
        await session.EnsureLoadedAsync(Ct);
        var rail = Render<NavMenu>(p => p.SignedIn());
        var guarded = Render<AdminOnly>(p => p.AddChildContent("<p id='admin'>admin content</p>"));
        rail.FindAll("a.ts-rail-link").Count.ShouldBe(7);

        Services.GetRequiredService<SessionExpiry>().Report();

        session.ExpiredWhileWorking.ShouldBeTrue();
        rail.WaitForAssertion(() => rail.FindAll("a.ts-rail-link").Count.ShouldBe(7));
        guarded.WaitForAssertion(() => guarded.Find("#admin").TextContent.ShouldBe("admin content"));
    }
}
