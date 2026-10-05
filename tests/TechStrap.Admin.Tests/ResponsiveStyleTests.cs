using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The responsive and accessibility rules (UX-BRIEF-admin, "Responsive and accessibility"): the rail folds away below 992px, the queue stacks below 768px, a wide table scrolls in its own region,
/// the composer follows the conversation on a phone, and forced colours keep what colour alone would say. Reads the compiled CSS, so a rule that is renamed, moved to the wrong breakpoint or
/// deleted fails here. How it looks is the owner's checklist in ADMIN-APP.md.
/// </summary>
public sealed class ResponsiveStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");
    private const string Narrow = "(max-width: 991.98px)";
    private const string Phone = "(max-width: 767.98px)";
    private const string Tablet = "(min-width: 768px)and (max-width: 991.98px)"; // the compressed output has no space before "and"

    [Fact]
    public void The_menu_button_is_hidden_on_a_wide_screen_and_a_44px_target_below_992px()
    {
        Css.OutsideMedia().Declarations(".ts-rail-toggle")["display"].ShouldBe("none");

        var narrow = Css.InMedia(Narrow).Declarations(".ts-rail-toggle");
        narrow["display"].ShouldBe("inline-flex");
        narrow["min-height"].ShouldBe("44px");
        narrow["min-width"].ShouldBe("44px");
    }

    [Fact]
    public void The_links_are_always_shown_on_a_wide_screen_and_folded_away_below_992px_until_the_button_opens_them()
    {
        Css.OutsideMedia().Declarations(".ts-rail-panel")["display"].ShouldBe("flex");

        var narrow = Css.InMedia(Narrow);
        narrow.Declarations(".ts-rail-panel")["display"].ShouldBe("none");
        narrow.Declarations(".ts-rail-panel[data-open=true]")["display"].ShouldBe("flex");
        narrow.Declarations(".ts-shell")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void The_old_820px_strip_is_gone_so_the_rail_has_one_breakpoint()
    {
        Css.Text.ShouldNotContain("@media(max-width: 820px){.ts-shell");
        Css.Text.ShouldNotContain(".ts-rail{flex-direction:row;align-items:center;overflow-x:auto");
    }

    [Fact]
    public void A_tablet_drops_product_requester_and_priority_in_the_queue_only()
    {
        var tablet = Css.InMedia(Tablet);

        // One rule for the cells and their headers: Product (4th), Requester (5th) and Priority (7th), in the queue's own table.
        tablet.Declarations(
            ".ts-ledger--stack .ts-col-product,.ts-ledger--stack .ts-col-requester,.ts-ledger--stack .ts-col-priority,"
            + ".ts-ledger--stack th:nth-child(4),.ts-ledger--stack th:nth-child(5),.ts-ledger--stack th:nth-child(7)")["display"].ShouldBe("none");

        // The settings tables share the .ts-ledger class and used to lose their 4th, 5th and 7th header cells at this width: nothing may select them any more.
        Css.Text.ShouldNotContain(".ts-ledger th:nth-child(4)");
        Css.Text.ShouldNotContain(".ts-ledger .ts-col-product");
    }

    [Fact]
    public void A_phone_stacks_each_ticket_into_a_card_with_every_field_back()
    {
        var phone = Css.InMedia(Phone);

        phone.Declarations(".ts-ledger--stack")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack tbody")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack tr")["display"].ShouldBe("flex");
        phone.Declarations(".ts-ledger--stack tr")["flex-wrap"].ShouldBe("wrap");
        phone.Declarations(".ts-ledger--stack td")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack .ts-col-subject")["flex"].ShouldBe("1 0 100%");
        phone.Declarations(".ts-ledger--stack td[data-label]::before")["content"].ShouldBe("attr(data-label) \": \"");

        // Nothing the tablet hid stays hidden on a phone, and the header row is clipped, not removed, so a screen reader still has the column names.
        phone.Declarations(".ts-ledger--stack .ts-col-product").ContainsKey("display").ShouldBeFalse();
        phone.Declarations(".ts-ledger--stack thead")["clip-path"].ShouldBe("inset(50%)");
        phone.Declarations(".ts-ledger--stack thead").ContainsKey("display").ShouldBeFalse();
    }

    [Fact]
    public void A_phone_reads_number_status_and_time_first_then_the_subject_then_the_details()
    {
        var phone = Css.InMedia(Phone);
        string Order(string cell) => phone.Declarations($".ts-ledger--stack .ts-col-{cell}")["order"];

        new[] { "number", "status", "activity", "subject", "product", "requester", "priority", "assignee", "actions" }
            .Select(Order).ShouldBe(["1", "2", "3", "4", "5", "6", "7", "8", "9"]);
    }

    [Fact]
    public void The_selected_row_keeps_a_bar_on_the_left_when_it_is_a_card()
    {
        Css.InMedia(Phone).Declarations(".ts-ledger--stack .ts-row--selected")["box-shadow"].ShouldBe("inset 3px 0 0 var(--margin)");
    }

    [Fact]
    public void On_a_phone_the_composer_follows_the_conversation_and_the_controls_come_after_it()
    {
        Css.InMedia("(max-width: 1100px)").Declarations(".ts-side")["order"].ShouldBe("-1");
        Css.InMedia(Phone).Declarations(".ts-side")["order"].ShouldBe("1");
        Css.InMedia("(max-width: 1100px)").Declarations(".ts-ticket-layout")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void A_scroll_region_scrolls_sideways_inside_its_own_box_and_shows_a_shadow_where_there_is_more()
    {
        var scroll = Css.Declarations(".ts-scroll");

        scroll["overflow-x"].ShouldBe("auto");
        scroll["max-width"].ShouldBe("100%");
        scroll["background"].ShouldContain("local");
        scroll["background"].ShouldContain("scroll");
    }

    [Fact]
    public void Forced_colours_keep_the_current_link_tab_and_row_by_an_outline_and_the_focused_scroll_region_by_a_ring()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        forced.Declarations(".ts-rail-link[aria-current=page],.ts-tab[aria-current=page]")["outline"].ShouldBe("2px solid Highlight");
        forced.Declarations(".ts-row--selected")["outline"].ShouldBe("2px solid Highlight");
        forced.Declarations(".ts-scroll:focus-visible")["outline"].ShouldBe("3px solid Highlight");
        forced.Declarations(".ts-rail-link,.ts-tab,.ts-palette-option")["border-color"].ShouldBe("Canvas");
    }

    [Fact]
    public void Reduced_motion_switches_every_animation_and_transition_off_and_nothing_scrolls_smoothly()
    {
        var everything = Css.InMedia("(prefers-reduced-motion: reduce)").Declarations("*,*::before,*::after");
        everything["animation"].ShouldBe("none !important");
        everything["transition"].ShouldBe("none !important");

        var styles = RepositoryRoot.Combine("src", "TechStrap.Admin", "Styles");
        Directory.EnumerateFiles(styles, "*.scss", SearchOption.TopDirectoryOnly)
            .Where(file => File.ReadAllText(file).Contains("scroll-behavior", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }
}
