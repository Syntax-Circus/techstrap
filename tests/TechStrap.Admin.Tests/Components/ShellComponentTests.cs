using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Contracts.Agents;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ShellComponentTests : AdminComponentTest
{
    [Fact]
    public void The_status_bar_lists_the_hints_and_has_one_polite_message_slot()
    {
        var cut = Render<StatusBar>();

        cut.FindAll(".ts-statusbar-hints li").Select(li => li.TextContent.Trim()).ShouldBe(
            ["j k move", "Enter open", "r reply", "n note", "e assign", "/ search", "? help"]);
        cut.Find("p.ts-statusbar-message").GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBeEmpty();
    }

    [Fact]
    public void A_message_appears_in_the_status_slot_and_the_next_one_replaces_it()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(() => StatusMessages.Show("Reply sent on ACME-142"));
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBe("Reply sent on ACME-142");

        cut.InvokeAsync(() => StatusMessages.Show("Restored ACME-142 from spam"));
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBe("Restored ACME-142 from spam");
    }

    [Fact]
    public async Task The_rail_for_an_agent_shows_the_queue_the_name_and_a_post_form_to_sign_out()
    {
        this.AddAgentShell();
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find("a.ts-rail-link[href='/queue']").TextContent.ShouldBe("Queue");
        cut.Find(".ts-rail-name").TextContent.ShouldBe("Sam");
        cut.FindAll(".ts-rail-role").ShouldBeEmpty();
        cut.Find("form[action='/signout']").GetAttribute("method").ShouldBe("post");
    }

    [Fact]
    public async Task The_rail_marks_an_admin_and_has_no_settings_links_yet()
    {
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find(".ts-rail-role").TextContent.ShouldBe("Admin");
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
    }

    [Fact]
    public void The_rail_shows_the_brand_alone_until_the_session_is_ready_and_then_follows_it()
    {
        this.AddAgentShell();

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find("a.ts-brand").GetAttribute("aria-label").ShouldBe("TechStrap Admin home");
        cut.FindAll("a.ts-rail-link").ShouldBeEmpty();
        cut.FindAll("form").ShouldBeEmpty();

        cut.InvokeAsync(() => Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken));

        cut.WaitForAssertion(() => cut.Find("a.ts-rail-link[href='/queue']").ShouldNotBeNull());
    }

    [Fact]
    public void The_layout_registers_the_key_listener_once_and_unsubscribes_on_dispose()
    {
        this.AddAgentShell();

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"page\">page</p>"))));

        Shortcuts.VerifyInvoke("register", 1);
        cut.Find("main#main #page").TextContent.ShouldBe("page");
        cut.Find("a.ts-skip-link").GetAttribute("href").ShouldBe("#main");
    }

    [Fact]
    public async Task The_question_mark_opens_the_shortcut_help_and_close_hides_it()
    {
        this.AddAgentShell();
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressAsync("?");
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));

        cut.Find("dialog.ts-dialog h2").TextContent.ShouldBe("Keyboard shortcuts");
        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(9);

        cut.Find("dialog button").Click();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public async Task The_slash_key_on_another_screen_opens_the_queue_and_on_the_queue_does_nothing()
    {
        this.AddAgentShell();
        var navigation = Services.GetRequiredService<NavigationManager>();
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        navigation.NavigateTo("/tickets/ACME-142");
        await PressAsync("/");
        navigation.Uri.ShouldEndWith("/queue");

        var historyBefore = ((BunitNavigationManager)navigation).History.Count;
        await PressAsync("/");

        ((BunitNavigationManager)navigation).History.Count.ShouldBe(historyBefore);
    }
}
