using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The ticket's "More actions" menu follows the WAI-ARIA menu button pattern: roles, a focus move on open, and keys handled by <c>menu.js</c>, which tells the component when to open or close.
/// The key handling itself is pinned by <c>menu.test.mjs</c>; here is the markup and the hand-over between the script and the component.
/// </summary>
public sealed class TicketActionsMenuTests : AdminComponentTest
{
    public TicketActionsMenuTests()
    {
        Services.AddSingleton(Substitute.For<ITicketsClient>());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
        Services.AddSingleton(_ => AgentSessions.SignedIn(admin: true));
    }

    private IRenderedComponent<TicketActions> RenderActions() =>
        Render<TicketActions>(p => p.Add(c => c.Ticket, TestData.Model()));

    [Fact]
    public void The_button_names_the_menu_and_the_menu_is_labelled_by_the_button()
    {
        var cut = RenderActions();

        var button = cut.Find(".ts-actions > button");
        var menu = cut.Find("ul.ts-menu");
        button.GetAttribute("aria-haspopup").ShouldBe("menu");
        button.GetAttribute("aria-controls").ShouldBe(menu.Id);
        menu.GetAttribute("role").ShouldBe("menu");
        menu.GetAttribute("aria-labelledby").ShouldBe(button.Id);
        button.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Every_entry_is_a_menu_item_that_the_script_focuses_and_the_tab_key_skips()
    {
        var cut = RenderActions();

        cut.FindAll("ul.ts-menu > li").ShouldAllBe(li => li.GetAttribute("role") == "none");
        var items = cut.FindAll("ul.ts-menu button");
        items.Count.ShouldBe(3);
        items.ShouldAllBe(button => button.GetAttribute("role") == "menuitem" && button.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public async Task The_keyboard_script_is_attached_once_to_the_wrapper()
    {
        var cut = RenderActions();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Menu.VerifyInvoke("attach", 1);

        cut.Find(".ts-actions > button").Click();
        cut.Find(".ts-actions > button").Click();

        Menu.VerifyInvoke("attach", 1);
    }

    [Fact]
    public void Opening_with_a_click_moves_focus_to_the_first_item_and_closing_does_not()
    {
        var cut = RenderActions();

        cut.Find(".ts-actions > button").Click();

        cut.WaitForAssertion(() => Menu.VerifyInvoke("focusFirst", 1));
        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("true");

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        Menu.VerifyInvoke("focusFirst", 1);
    }

    [Fact]
    public async Task ArrowDown_on_the_closed_button_opens_the_menu_and_focuses_the_first_item()
    {
        var cut = RenderActions();

        await cut.InvokeAsync(() => cut.Instance.OpenMenu());

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.WaitForAssertion(() => Menu.VerifyInvoke("focusFirst", 1));
    }

    [Fact]
    public async Task Escape_and_Tab_close_the_menu()
    {
        var cut = RenderActions();
        cut.Find(".ts-actions > button").Click();

        await cut.InvokeAsync(() => cut.Instance.CloseMenu());

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Fact]
    public async Task A_script_that_fails_never_breaks_the_menu_for_the_mouse()
    {
        Menu.SetupVoid("attach", _ => true).SetException(new JSException("no script"));
        var cut = RenderActions();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
    }
}
