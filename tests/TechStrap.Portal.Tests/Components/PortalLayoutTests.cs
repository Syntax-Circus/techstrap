using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Components.Layout;
using TechStrap.Portal.Components.Ui;
using TechStrap.Portal.Tests.Routing;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Components;

public sealed class PortalLayoutTests : BunitContext
{
    private static ProductThemeViewModel Theme(string name = "Paperplane", string? accent = "#F59E0B", string? logo = "https://cdn.example.com/paperplane.png") =>
        new("paperplane", name, accent, logo);

    private ProductScope Scope { get; } = new();

    public PortalLayoutTests()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions()));
        Services.AddSingleton(Scope);
        Services.AddPortalLinks();
    }

    private IRenderedComponent<PortalLayout> RenderLayout() => Render<PortalLayout>(p => p.Add(l => l.Body, "<p id=\"page\">the page</p>"));

    [Fact]
    public void Without_a_product_the_layout_is_neutral_no_header_no_product_footer_no_accent_and_the_powered_by_line()
    {
        var cut = RenderLayout();

        cut.Find("#page").TextContent.ShouldBe("the page");
        cut.FindAll("header").ShouldBeEmpty();
        cut.FindAll("nav.ts-product-footer").ShouldBeEmpty();
        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
        cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
        cut.Markup.ShouldNotContain("--ts-accent");
        cut.Markup.ShouldNotContain("<img src=\"https");
    }

    [Fact]
    public void With_a_product_the_layout_gets_its_header_footer_and_accent_around_the_page_and_keeps_one_powered_by_line()
    {
        Scope.Set(Theme());

        var cut = RenderLayout();

        cut.Find("div.ts-accent-scope").GetAttribute("style").ShouldBe("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        var header = cut.Find("header.ts-product-header");
        header.QuerySelector("a.ts-product-name")!.TextContent.Trim().ShouldBe("Paperplane");
        header.QuerySelector("a.ts-product-name")!.GetAttribute("href").ShouldBe("/p/paperplane");
        header.QuerySelector("img.ts-product-logo")!.GetAttribute("src").ShouldBe("https://cdn.example.com/paperplane.png");
        cut.Find("main #page").TextContent.ShouldBe("the page");
        cut.Find("nav.ts-product-footer a").GetAttribute("href").ShouldBe("/p/paperplane/lost-link");
        cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
        cut.Markup.IndexOf("ts-product-header", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal));
        cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("ts-product-footer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_that_sets_the_product_after_the_layout_rendered_updates_the_layout()
    {
        var cut = RenderLayout();
        cut.FindAll("header").ShouldBeEmpty();

        await cut.InvokeAsync(() => Scope.Set(Theme()));

        cut.Find("header.ts-product-header a.ts-product-name").TextContent.Trim().ShouldBe("Paperplane");
        cut.Find("div.ts-accent-scope").GetAttribute("style")!.ShouldContain("--ts-accent:#F59E0B");
    }

    [Fact]
    public void The_logo_is_decorative_because_the_name_sits_beside_it_and_is_omitted_when_the_theme_has_none()
    {
        Scope.Set(Theme());
        var withLogo = RenderLayout();
        withLogo.Find("img.ts-product-logo").GetAttribute("alt").ShouldBe(string.Empty);

        Scope.Set(Theme(logo: null));
        var withoutLogo = RenderLayout();
        withoutLogo.FindAll("img.ts-product-logo").ShouldBeEmpty();
    }

    [Fact]
    public void The_product_name_is_text_never_markup()
    {
        Scope.Set(Theme(name: "<img src=x onerror=alert(1)>Paper & \"plane\""));

        var cut = RenderLayout();

        cut.FindAll("header img[onerror]").ShouldBeEmpty();
        cut.Find("a.ts-product-name").TextContent.Trim().ShouldBe("<img src=x onerror=alert(1)>Paper & \"plane\"");
        cut.Markup.ShouldNotContain("<img src=x");
    }

    [Fact]
    public void An_unusable_accent_gives_no_style_but_the_header_still_shows()
    {
        Scope.Set(Theme(accent: null));

        var cut = RenderLayout();

        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
        cut.Find("header.ts-product-header").ShouldNotBeNull();
    }

    [Fact]
    public async Task Disposing_the_layout_stops_it_listening_to_the_scope()
    {
        var cut = RenderLayout();

        SubscriberCount(Scope).ShouldBe(1, "the rendered layout listens");
        await DisposeComponentsAsync();

        SubscriberCount(Scope).ShouldBe(0, "a disposed layout must not stay subscribed to a scope that outlives it");
        Should.NotThrow(() => Scope.Set(Theme()));
        cut.ShouldNotBeNull();
    }

    private static int SubscriberCount(ProductScope scope) =>
        ((Delegate?)typeof(ProductScope).GetField(nameof(ProductScope.Changed), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope))?.GetInvocationList().Length ?? 0;

    [Fact]
    public void The_powered_by_line_follows_the_installation_setting_and_the_product_pages_keep_their_own_footer()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions { Show = false }));
        Scope.Set(Theme());

        var cut = RenderLayout();

        cut.FindAll("footer.ts-powered").ShouldBeEmpty();
        cut.FindAll("nav.ts-product-footer").Count.ShouldBe(1);
    }
}
