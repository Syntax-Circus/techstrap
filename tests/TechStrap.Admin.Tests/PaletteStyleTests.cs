using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The palette's selected line is set apart by a fill, a bar and weight, and by an outline when forced colours drop the fill. Colour is never the only cue.</summary>
public sealed class PaletteStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_selected_line_has_a_fill_a_bar_and_a_heavier_weight()
    {
        var selected = Css.Declarations(".ts-palette-option[aria-selected=true]");

        selected["background"].ShouldBe("var(--sel)");
        selected["border-left-color"].ShouldBe("var(--margin)");
        selected["font-weight"].ShouldBe("700");
    }

    [Fact]
    public void In_forced_colours_the_selected_line_gets_an_outline_in_the_system_highlight_colour()
    {
        var forced = Css.Declarations(".ts-palette-option[aria-selected=true]");

        forced["outline"].ShouldBe("2px solid Highlight");
    }

    [Fact]
    public void The_palette_fits_a_phone_and_scrolls_its_list_not_the_page()
    {
        Css.Declarations(".ts-palette")["width"].ShouldBe("min(36rem,100vw - 32px)");
        Css.Declarations(".ts-palette-list")["overflow-y"].ShouldBe("auto");
    }
}
