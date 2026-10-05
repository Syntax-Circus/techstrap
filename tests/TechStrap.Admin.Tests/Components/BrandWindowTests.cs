using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The retro window frame of docs/BRAND.md section 18: beige plate, title bar with a LED and a short title, the head mark, one wink, one way back. No fake OS chrome.</summary>
public sealed class BrandWindowTests : BunitContext
{
    private IRenderedComponent<BrandWindow> RenderWindow() =>
        Render<BrandWindow>(p => p
            .Add(w => w.Title, "queue.exe — 0 items")
            .Add(w => w.Heading, "All caught up")
            .AddChildContent("<p>Zero tickets, fully supported.</p><a class=\"ts-window-link\" href=\"/\">View open tickets</a>"));

    [Fact]
    public void Shows_title_bar_heading_head_mark_and_the_content()
    {
        var cut = RenderWindow();

        cut.Find("article.ts-window").GetAttribute("aria-label").ShouldBe("All caught up");
        cut.Find(".ts-window-title").TextContent.ShouldBe("queue.exe — 0 items");
        cut.Find(".ts-window-led").GetAttribute("aria-hidden").ShouldBe("true");
        cut.Find(".ts-window-body h2").TextContent.ShouldBe("All caught up");
        cut.Find(".ts-window-body p").TextContent.ShouldBe("Zero tickets, fully supported.");
        cut.Find(".ts-window-link").TextContent.ShouldBe("View open tickets");
    }

    [Fact]
    public void The_heading_is_an_h2_by_default_and_an_h1_when_the_window_is_the_whole_page()
    {
        var inside = RenderWindow();
        var standalone = Render<BrandWindow>(p => p
            .Add(w => w.Title, "ERROR 404")
            .Add(w => w.Heading, "This page fell out of its strap.")
            .Add(w => w.HeadingLevel, 1));

        inside.FindAll(".ts-window-body h1").ShouldBeEmpty();
        inside.Find(".ts-window-body h2").TextContent.ShouldBe("All caught up");
        standalone.FindAll(".ts-window-body h2").ShouldBeEmpty();
        standalone.Find(".ts-window-body h1").TextContent.ShouldBe("This page fell out of its strap.");
    }

    [Fact]
    public void The_mascot_is_the_96px_SVG_head_mark_and_decorative()
    {
        var mark = RenderWindow().Find(".ts-window-body img");

        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
        mark.GetAttribute("width").ShouldBe("96");
        mark.GetAttribute("height").ShouldBe("96");
    }

    [Fact]
    public void The_title_bar_has_no_close_minimise_or_maximise_buttons()
    {
        var cut = RenderWindow();

        cut.FindAll(".ts-window-titlebar button, .ts-window-titlebar a, .ts-window-titlebar [role=button]").ShouldBeEmpty();
    }
}
