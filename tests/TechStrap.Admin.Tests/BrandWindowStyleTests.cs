using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

public sealed class BrandWindowStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_window_is_a_beige_plate_with_a_2px_edge_a_hard_shadow_and_square_corners()
    {
        var window = Css.Declarations(".ts-window");

        window["background"].ShouldBe("var(--bm-plate)");
        window["border"].ShouldBe("2px solid var(--bm-edge)");
        window["box-shadow"].ShouldBe("3px 3px 0 var(--bm-shadow)");
        window.ContainsKey("border-radius").ShouldBeFalse();
        window["max-width"].ShouldBe("400px");
    }

    [Fact]
    public void The_title_bar_is_navy_mono_with_a_green_LED()
    {
        Css.Declarations(".ts-window-titlebar")["background"].ShouldBe("var(--bm-bar)");
        Css.Declarations(".ts-window-titlebar")["color"].ShouldBe("var(--bm-on-bar)");
        Css.Declarations(".ts-window-led")["background"].ShouldBe("var(--bm-led)");
        Css.Declarations(".ts-window-led")["border-radius"].ShouldBe("50%");
    }

    [Fact]
    public void The_button_is_CRT_blue_and_presses_in_by_one_pixel()
    {
        Css.Declarations(".ts-window-button")["background"].ShouldBe("var(--bm-crt)");
        Css.Declarations(".ts-window-button:active")["transform"].ShouldBe("translate(1px, 1px)");
    }

    [Fact]
    public void The_window_shrinks_to_the_viewport_and_never_overflows_at_320px()
    {
        Css.Declarations(".ts-window")["width"].ShouldBe("100%");
        Css.Declarations(".ts-window-body")["min-width"].ShouldBe("0");
        Css.Declarations(".ts-window-body p")["overflow-wrap"].ShouldBe("anywhere");
    }
}
