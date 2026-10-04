using Bunit;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class MainLayoutTests : AdminComponentTest
{
    public MainLayoutTests() => this.AddAgentShell();

    [Fact]
    public void Header_shows_the_SVG_head_mark_and_no_mascot_copy()
    {
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        var mark = cut.Find(".ts-brand img");
        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
        cut.Find(".ts-brand span").TextContent.ShouldContain("TechStrap");
    }

    [Fact]
    public void Page_content_renders_inside_main_under_an_error_boundary()
    {
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find("nav[aria-label='Admin navigation']").ShouldNotBeNull();
    }

    [Fact]
    public void A_page_that_throws_shows_the_plain_error_view_instead_of_crashing_the_shell()
    {
        RenderFragment throwing = builder =>
        {
            builder.OpenComponent<Throwing>(0);
            builder.CloseComponent();
        };

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, throwing));

        var error = cut.Find("section.ts-error");
        error.QuerySelector("h1")!.TextContent.ShouldBe("Couldn't load this screen.");
        error.TextContent.ShouldNotContain("InvalidOperationException");
        cut.Find(".ts-brand").ShouldNotBeNull();
    }

    private sealed class Throwing : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            throw new InvalidOperationException("boom");
    }
}
