using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The knowledge base screens (PHASE-08): the editor shows Write and Preview side by side from 992 px and one at a time below it, a picture can never be wider than its pane, and text that comes from agents wraps
/// instead of stretching the page. Reads the compiled CSS, so a rule that is renamed or moved to the wrong breakpoint fails here. How it looks is the owner's checklist.
/// </summary>
public sealed class KbStyleTests
{
    private const string Wide = "(min-width: 992px)";

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Below_992px_only_the_chosen_pane_is_shown()
    {
        var outside = Css.OutsideMedia();

        outside.Declarations(".ts-kb-md[data-pane=write] .ts-kb-pane--preview,.ts-kb-md[data-pane=preview] .ts-kb-pane--write")["display"].ShouldBe("none");
        outside.Declarations(".ts-kb-tabs")["display"].ShouldBe("flex");
        outside.Declarations(".ts-kb-panes")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void From_992px_both_panes_are_shown_side_by_side_and_the_buttons_that_choose_between_them_go()
    {
        var wide = Css.InMedia(Wide);

        wide.Declarations(".ts-kb-tabs")["display"].ShouldBe("none");
        wide.Declarations(".ts-kb-panes")["grid-template-columns"].ShouldBe("minmax(0, 1fr) minmax(0, 1fr)");
        wide.Declarations(".ts-kb-md[data-pane] .ts-kb-pane")["display"].ShouldBe("block");
    }

    [Fact]
    public void A_picture_or_a_wide_table_in_the_preview_can_never_stretch_its_pane()
    {
        Css.Declarations(".ts-kb-preview-body img")["max-width"].ShouldBe("100%");
        Css.Declarations(".ts-kb-preview-body img")["height"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview-body table")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview-body pre")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview")["overflow-wrap"].ShouldBe("anywhere");
    }

    [Fact]
    public void The_article_text_is_monospace_and_the_two_panes_can_shrink_below_their_content()
    {
        Css.Declarations(".ts-kb-textarea")["font-family"].ShouldBe("var(--ts-font-mono)");
        Css.Declarations(".ts-kb-pane")["min-width"].ShouldBe("0");
    }
}
