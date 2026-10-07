using Bunit;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Tests.Components;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The indicator sits in <c>MainLayout</c>, which is outside the agent gate: it draws and starts the connection only once the gate has admitted the agent.</summary>
public sealed class MainLayoutLiveTests : AdminComponentTest
{
    public MainLayoutLiveTests() => this.AddAgentShell();

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"page\">page</p>"))));

    [Fact]
    public void The_layout_draws_one_indicator_and_starts_the_connection_once_the_agent_is_admitted()
    {
        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.FindAll(".ts-live").Count.ShouldBe(1));
        LiveClient.StartCalls.ShouldBe(1);
        cut.Find("main.ts-main #page").TextContent.ShouldBe("page");
    }

    [Fact]
    public void Switched_off_the_layout_draws_no_indicator_and_starts_nothing()
    {
        LiveClient.IsEnabled = false;

        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.FindAll(".ts-live").ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public void A_failing_client_leaves_the_layout_and_the_page_working()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");

        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find(".ts-brand").ShouldNotBeNull();
    }
}
