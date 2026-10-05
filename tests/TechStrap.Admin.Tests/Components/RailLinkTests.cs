using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The rail says which page is current with <c>aria-current="page"</c>, for a screen reader and for the rail's own style.</summary>
public sealed class RailLinkTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("queue", "/queue", true)]
    [InlineData("queue/mine", "/queue", true)]
    [InlineData("queue/mine?search=reset&page=2", "/queue", true)]
    [InlineData("QUEUE/Mine", "/queue", true)]
    [InlineData("queue#top", "/queue", true)]
    [InlineData("settings/products/abc", "/settings/products", true)]
    [InlineData("queued", "/queue", false)]
    [InlineData("queue-archive/old", "/queue", false)]
    [InlineData("settings/agents", "/settings/products", false)]
    [InlineData("tickets/ORB-42", "/queue", false)]
    [InlineData("", "/queue", false)]
    [InlineData("account/notifications", "/account/notifications", true)]
    public void The_address_is_current_when_it_is_the_link_or_below_it_on_whole_segments(string relativePath, string href, bool expected) =>
        RailLink.IsCurrentAddress(relativePath, href).ShouldBe(expected);

    [Fact]
    public void The_link_in_the_current_section_is_marked_and_the_others_are_not()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings/products/abc?tab=keys");

        var cut = Render<RailLink>(p => p.Add(l => l.Href, "/settings/products").AddChildContent("Products"));
        var other = Render<RailLink>(p => p.Add(l => l.Href, "/settings/agents").AddChildContent("Agents"));

        cut.Find("a.ts-rail-link").GetAttribute("aria-current").ShouldBe("page");
        cut.Find("a.ts-rail-link").GetAttribute("href").ShouldBe("/settings/products");
        other.Find("a.ts-rail-link").HasAttribute("aria-current").ShouldBeFalse();
    }

    [Fact]
    public async Task The_mark_moves_when_the_agent_navigates()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/queue");
        var cut = Render<RailLink>(p => p.Add(l => l.Href, "/settings/tags").AddChildContent("Tags"));
        cut.Find("a").HasAttribute("aria-current").ShouldBeFalse();

        await cut.InvokeAsync(() => navigation.NavigateTo("/settings/tags"));

        cut.WaitForAssertion(() => cut.Find("a").GetAttribute("aria-current").ShouldBe("page"));

        await cut.InvokeAsync(() => navigation.NavigateTo("/queue/mine"));

        cut.WaitForAssertion(() => cut.Find("a").HasAttribute("aria-current").ShouldBeFalse());
    }

    [Fact]
    public async Task In_the_rail_exactly_one_link_is_current_and_it_is_the_one_for_the_page()
    {
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/audit");

        var cut = Render<NavMenu>(p => p.SignedIn());

        var current = cut.FindAll("a.ts-rail-link[aria-current=page]");
        current.Count.ShouldBe(1);
        current[0].GetAttribute("href").ShouldBe("/settings/audit");
    }
}
