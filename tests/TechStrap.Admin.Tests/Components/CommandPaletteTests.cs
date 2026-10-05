using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3, the command palette: Ctrl+K opens it from anywhere, a plain agent is never shown an admin command, ArrowDown, ArrowUp and Enter work the list, and a command fires once.
/// Every test goes through <c>MainLayout</c>, which owns the palette, with the key arriving the way <c>shortcuts.js</c> reports it.
/// </summary>
public sealed class CommandPaletteTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private IAgentsClient _agents = default!;
    private int _ran;

    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private async Task<IRenderedComponent<MainLayout>> RenderLayoutAsync(string role = AgentRoles.Agent)
    {
        _agents = this.AddAgentShell(role);
        await Session.EnsureLoadedAsync(Ct);
        return Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
    }

    private async Task<IRenderedComponent<MainLayout>> OpenPaletteAsync(string role = AgentRoles.Agent)
    {
        var cut = await RenderLayoutAsync(role);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => cut.FindAll("dialog[data-palette] [role=option]").ShouldNotBeEmpty());
        return cut;
    }

    private Task PressCtrlKAsync() => PressAsync("k", ctrl: true, typing: true);

    private static IReadOnlyList<string> Options(IRenderedComponent<MainLayout> cut) =>
        cut.FindAll("dialog[data-palette] [role=option] .ts-palette-label").Select(o => o.TextContent).ToList();

    private static string? Selected(IRenderedComponent<MainLayout> cut) =>
        cut.FindAll("dialog[data-palette] [role=option]").SingleOrDefault(o => o.GetAttribute("aria-selected") == "true")?.QuerySelector(".ts-palette-label")?.TextContent;

    private static Task KeyAsync(IRenderedComponent<MainLayout> cut, string key) =>
        cut.InvokeAsync(() => cut.FindComponent<CommandPalette>().Instance.NavigateKey(key));

    private PaletteCommand Counting(string id = "probe", bool adminOnly = false, Func<Task>? run = null) =>
        new(id, "Probe " + id, "Test", run ?? (() =>
        {
            _ran++;
            return Task.CompletedTask;
        }), AdminOnly: adminOnly);

    [Fact]
    public async Task Ctrl_K_opens_the_palette_with_focus_on_the_input_and_closes_it_again()
    {
        var cut = await RenderLayoutAsync();
        Dialogs.VerifyNotInvoke("open");

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));
        var dialogOpen = Dialogs.Invocations["open"].Single();
        dialogOpen.Arguments.Count.ShouldBe(3);
        cut.Find("dialog[data-palette] input[role=combobox]").GetAttribute("aria-expanded").ShouldBe("true");

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public async Task Ctrl_K_works_while_single_key_shortcuts_are_switched_off()
    {
        var cut = await RenderLayoutAsync();
        ShortcutService.SingleKeyEnabled = false;

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));
    }

    [Fact]
    public async Task A_plain_agent_is_offered_the_queue_views_and_my_settings_and_never_an_admin_page()
    {
        var cut = await OpenPaletteAsync(AgentRoles.Agent);

        Options(cut).ShouldBe(
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        var palette = cut.Find("dialog[data-palette]").InnerHtml;
        foreach (var admin in new[] { "Products", "Agents", "Tags", "Audit", "Failed emails", "/settings", "/ops" })
        {
            palette.ShouldNotContain(admin);
        }
    }

    [Fact]
    public async Task A_plain_agent_never_sees_an_admin_command_that_a_screen_registered()
    {
        var cut = await RenderLayoutAsync(AgentRoles.Agent);
        Registry.Register([Counting("plain"), Counting("secret", adminOnly: true)]);

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe plain"));
        Options(cut).ShouldNotContain("Probe secret");
    }

    [Fact]
    public async Task An_admin_is_offered_the_admin_pages_too_and_each_shows_its_group()
    {
        var cut = await OpenPaletteAsync(AgentRoles.Admin);

        Options(cut).ShouldContain("Products");
        Options(cut).ShouldContain("Failed emails");
        Options(cut).Count.ShouldBe(12);
        cut.FindAll(".ts-palette-group").Select(g => g.TextContent).Distinct().ShouldBe(["Go to", "Admin"]);
    }

    [Fact]
    public async Task It_does_not_open_until_the_api_has_said_who_the_agent_is()
    {
        _agents = this.AddAgentShell();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("agent-inactive", "Not an agent.", ResultErrorKind.Forbidden));
        await Session.EnsureLoadedAsync(Ct);
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressCtrlKAsync();

        Session.State.ShouldBe(AgentSessionState.NoAccess);
        Dialogs.VerifyNotInvoke("open");
        cut.FindAll("dialog[data-palette] [role=option]").ShouldBeEmpty();
    }

    [Fact]
    public async Task Typing_filters_the_list_and_selects_the_first_match()
    {
        var cut = await OpenPaletteAsync();

        cut.Find("dialog[data-palette] input").Input("mi");

        Options(cut).ShouldBe(["Queue: Mine"]);
        Selected(cut).ShouldBe("Queue: Mine");
        cut.Find("dialog[data-palette] [role=status]").TextContent.ShouldBe("1 command");
    }

    [Fact]
    public async Task No_match_says_so_and_Enter_does_nothing()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        cut.Find("dialog[data-palette] input").Input("zzzz");
        await KeyAsync(cut, "Enter");

        cut.FindAll("dialog[data-palette] [role=option]").ShouldBeEmpty();
        cut.Find(".ts-palette-empty").TextContent.ShouldBe("No command matches.");
        cut.Find("dialog[data-palette] input").HasAttribute("aria-activedescendant").ShouldBeFalse();
        navigation.Uri.ShouldBe(before);
    }

    [Fact]
    public async Task The_arrow_keys_move_the_selection_and_wrap_and_the_input_names_the_active_option()
    {
        var cut = await OpenPaletteAsync();
        Selected(cut).ShouldBe("Queue: Unassigned");

        await KeyAsync(cut, "ArrowDown");
        Selected(cut).ShouldBe("Queue: Mine");
        cut.Find("dialog[data-palette] input").GetAttribute("aria-activedescendant")
            .ShouldBe(cut.FindAll("[role=option]").Single(o => o.GetAttribute("aria-selected") == "true").Id);

        await KeyAsync(cut, "ArrowUp");
        await KeyAsync(cut, "ArrowUp");
        Selected(cut).ShouldBe("My settings");

        await KeyAsync(cut, "ArrowDown");
        Selected(cut).ShouldBe("Queue: Unassigned");

        await KeyAsync(cut, "End");
        Selected(cut).ShouldBe("My settings");

        await KeyAsync(cut, "Home");
        Selected(cut).ShouldBe("Queue: Unassigned");
    }

    [Fact]
    public async Task Typing_puts_the_selection_back_on_the_first_line()
    {
        var cut = await OpenPaletteAsync();
        await KeyAsync(cut, "End");

        cut.Find("dialog[data-palette] input").Input("queue");

        Selected(cut).ShouldBe("Queue: Unassigned");
    }

    [Fact]
    public async Task Enter_runs_the_selected_command_after_the_dialog_has_closed()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        await KeyAsync(cut, "ArrowDown");

        await KeyAsync(cut, "Enter");

        cut.WaitForAssertion(() => new Uri(navigation.Uri).AbsolutePath.ShouldBe("/queue/mine"));
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public async Task Clicking_a_line_runs_it()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("My settings", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => new Uri(navigation.Uri).AbsolutePath.ShouldBe("/account/notifications"));
    }

    [Fact]
    public async Task A_command_fires_exactly_once_even_when_Enter_arrives_twice_and_the_page_renders_again_afterwards()
    {
        var cut = await RenderLayoutAsync();
        Registry.Register([Counting()]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe probe"));
        cut.Find("dialog[data-palette] input").Input("probe");

        await cut.InvokeAsync(() => Task.WhenAll(
            cut.FindComponent<CommandPalette>().Instance.NavigateKey("Enter"),
            cut.FindComponent<CommandPalette>().Instance.NavigateKey("Enter")));
        cut.WaitForAssertion(() => _ran.ShouldBe(1));

        // The palette draws again (every render of the layout does that): the finished command must not run a second time.
        cut.FindComponent<CommandPalette>().Render();
        cut.FindComponent<CommandPalette>().Render();
        await cut.InvokeAsync(() => Task.CompletedTask);

        _ran.ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_click_on_an_admin_line_does_nothing_once_the_session_is_no_longer_an_admins()
    {
        var cut = await RenderLayoutAsync(AgentRoles.Admin);
        Registry.Register([Counting("secret", adminOnly: true)]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe secret"));
        var staleLine = cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("Probe secret", StringComparison.Ordinal));

        // The API now says this agent is no longer an Admin; the palette has not drawn again yet, so the line is still on screen.
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));
        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));
        Session.IsAdmin.ShouldBeFalse();
        staleLine.Click();
        await cut.InvokeAsync(() => Task.CompletedTask);

        _ran.ShouldBe(0);
    }

    [Fact]
    public async Task Esc_closes_the_palette_and_a_new_opening_starts_empty()
    {
        var cut = await OpenPaletteAsync();
        cut.Find("dialog[data-palette] input").Input("mine");

        cut.Find("dialog[data-palette]").TriggerEvent("oncancel", EventArgs.Empty);
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));

        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 2));
        cut.Find("dialog[data-palette] input").GetAttribute("value").ShouldBeNullOrEmpty();
        Options(cut).Count.ShouldBe(7);
    }

    [Fact]
    public async Task A_browser_that_closed_the_dialog_by_itself_closes_the_palette()
    {
        var cut = await OpenPaletteAsync();

        await cut.InvokeAsync(() => cut.FindComponent<CommandPalette>().Instance.NativeClosed());

        cut.WaitForAssertion(() => Options(cut).ShouldBeEmpty());
    }

    [Fact]
    public async Task A_script_that_fails_leaves_the_palette_shut_and_the_page_alive()
    {
        var cut = await RenderLayoutAsync();
        Dialogs.SetupVoid("open", _ => true).SetException(new JSException("no dialog"));

        await PressCtrlKAsync();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.WaitForAssertion(() => Options(cut).ShouldBeEmpty());
        cut.Find(".ts-brand").ShouldNotBeNull();
        await PressCtrlKAsync();
        await cut.InvokeAsync(() => Task.CompletedTask);
    }

    [Fact]
    public async Task A_command_that_throws_is_reported_in_the_status_bar_and_does_not_end_the_circuit()
    {
        var cut = await RenderLayoutAsync();
        Registry.Register([Counting("boom", run: () => throw new InvalidOperationException("secret detail"))]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe boom"));
        cut.Find("dialog[data-palette] input").Input("boom");

        await KeyAsync(cut, "Enter");

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Couldn't run that command."));
        cut.Find(".ts-statusbar").TextContent.ShouldNotContain("secret detail");
    }
}
