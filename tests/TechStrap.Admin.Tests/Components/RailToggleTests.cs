using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Below 992px the rail folds behind a Menu button (the CSS decides when; see <c>ResponsiveStyleTests</c>). The button says whether the links are showing with <c>aria-expanded</c>, names
/// the panel it controls, and choosing a link folds the rail away again. A visitor with no session sees the brand and no button.
/// </summary>
public sealed class RailToggleTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private async Task<IRenderedComponent<NavMenu>> RenderRailAsync(string role = AgentRoles.Agent)
    {
        this.AddAgentShell(role);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);
        return Render<NavMenu>(p => p.SignedIn());
    }

    [Theory]
    [InlineData(AgentRoles.Agent)]
    [InlineData(AgentRoles.Admin)]
    public async Task The_button_starts_closed_and_controls_the_panel_that_holds_every_link(string role)
    {
        var cut = await RenderRailAsync(role);

        var button = cut.Find("button.ts-rail-toggle");
        var panel = cut.Find(".ts-rail-panel");
        button.TextContent.Trim().ShouldBe("Menu");
        button.GetAttribute("type").ShouldBe("button");
        button.GetAttribute("aria-expanded").ShouldBe("false");
        button.GetAttribute("aria-controls").ShouldBe(panel.Id);
        panel.GetAttribute("data-open").ShouldBe("false");
        cut.FindAll("a.ts-rail-link").ShouldAllBe(link => link.Closest(".ts-rail-panel") != null);
        cut.Find("a.ts-brand").Closest(".ts-rail-panel").ShouldBeNull();
    }

    [Fact]
    public async Task Pressing_the_button_opens_the_panel_and_pressing_it_again_closes_it()
    {
        var cut = await RenderRailAsync();

        cut.Find("button.ts-rail-toggle").Click();

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("true");

        cut.Find("button.ts-rail-toggle").Click();

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false");
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("false");
    }

    [Fact]
    public async Task Choosing_a_page_folds_the_rail_away()
    {
        var cut = await RenderRailAsync();
        cut.Find("button.ts-rail-toggle").Click();

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/account/notifications"));

        cut.WaitForAssertion(() => cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false"));
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("false");
    }

    [Fact]
    public async Task A_closed_rail_that_navigates_is_left_alone()
    {
        var cut = await RenderRailAsync();

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/queue/mine"));

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Fact]
    public void Before_the_session_is_known_the_rail_is_the_brand_alone_with_no_button()
    {
        this.AddAgentShell();

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.FindAll("button.ts-rail-toggle").ShouldBeEmpty();
        cut.FindAll(".ts-rail-panel").ShouldBeEmpty();
        cut.Find("a.ts-brand").ShouldNotBeNull();
    }
}
