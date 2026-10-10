using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// The Portal's layout and accessibility rules (PHASE-09 T16, UX-BRIEF-portal, BRAND.md sections 14, 17 and 24), read from the compiled CSS so a rule that is renamed, moved to the wrong breakpoint or deleted fails
/// here: one token for the reading width and one for the wide container, one column on a phone and cards from a tablet up, a 44px target for every link a visitor has to hit, a skip link that appears on focus,
/// forced colors that keep the 3px focus outline, and no motion at all for a visitor who asked for none. Every class the markup uses has a rule. How it looks is the owner's checklist in PORTAL-APP.md.
/// </summary>
public sealed partial class ResponsiveStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");
    private const string Root = ":root,[data-bs-theme=light]";
    private const string Phone = "(max-width: 479.98px)";

    [GeneratedRegex(@"(?<selectors>[^{}@]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex Rule();

    /// <summary>The declarations of every rule (outside or inside a media block) whose selector LIST contains <paramref name="selector"/>, merged, last wins.</summary>
    private static IReadOnlyDictionary<string, string> Containing(CompiledCss css, string selector)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match rule in Rule().Matches(css.Text))
        {
            if (!rule.Groups["selectors"].Value.Split(',').Select(s => s.Trim()).Contains(selector))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    merged[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
                }
            }
        }

        return merged;
    }

    // ---- widths ----

    [Fact]
    public void The_reading_column_and_the_wide_container_are_one_token_each()
    {
        var root = Css.Declarations(Root);

        root["--ts-reading-width"].ShouldBe("40rem");
        root["--ts-wide-width"].ShouldBe("64rem");
        Css.OutsideMedia().Declarations(".ts-reading")["max-width"].ShouldBe("var(--ts-reading-width)");
        foreach (var wide in new[] { ".ts-portal-main", ".ts-product-header", ".ts-product-footer" })
        {
            Containing(Css, wide)["max-width"].ShouldBe("var(--ts-wide-width)", wide);
        }

        Css.Declarations(".shell-placeholder")["max-width"].ShouldBe("var(--ts-reading-width)");
    }

    [Fact]
    public void No_rule_of_the_portal_writes_the_old_640px_width_any_more()
    {
        Css.Text.ShouldNotContain("max-width:640px");
        foreach (var file in Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly))
        {
            File.ReadAllText(file).ShouldNotContain("640px", customMessage: file);
        }
    }

    // ---- breakpoints ----

    [Fact]
    public void The_help_centre_home_is_one_column_on_a_phone_two_cards_across_from_768px_and_three_from_1200px()
    {
        Css.OutsideMedia().Declarations(".ts-kb-list--cards").ContainsKey("display").ShouldBeFalse("below 768px the list is the plain single column");
        var tablet = Css.InMedia("(min-width: 768px)").Declarations(".ts-kb-list--cards");
        tablet["display"].ShouldBe("grid");
        tablet["grid-template-columns"].ShouldBe("repeat(2, minmax(0, 1fr))");
        Css.InMedia("(min-width: 1200px)").Declarations(".ts-kb-list--cards")["grid-template-columns"].ShouldBe("repeat(3, minmax(0, 1fr))");
    }

    [Fact]
    public void On_a_phone_the_search_box_stacks_and_the_primary_action_of_a_form_fills_the_row()
    {
        var phone = Css.InMedia(Phone);

        phone.Declarations(".ts-help-search-row")["flex-direction"].ShouldBe("column");
        phone.Declarations(".ts-help-search-row .btn")["width"].ShouldBe("100%");
        phone.Declarations(".ts-form .btn,.ts-help-contact .btn")["width"].ShouldBe("100%");
        Css.OutsideMedia().Declarations(".ts-help-search-row")["display"].ShouldBe("flex");
        Css.OutsideMedia().Declarations(".ts-help-search-row .form-control")["min-width"].ShouldBe("0", "the box may shrink; it must never push the page wider than the screen");
    }

    [Fact]
    public void Nothing_a_visitor_reads_can_widen_the_page_a_long_word_wraps_and_a_table_or_code_block_scrolls_in_its_own_box()
    {
        Css.Declarations(".ts-kb-article-body")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-message-body")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-kb-article-body pre,.ts-kb-article-body table")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-article-body img")["max-width"].ShouldBe("100%");
        Css.Declarations(".ts-portal-main").ContainsKey("overflow-x").ShouldBeFalse("clipping the page would hide a problem instead of fixing it");
    }

    [Fact]
    public void The_page_fills_the_dynamic_viewport_so_a_phones_toolbar_does_not_leave_a_gap()
    {
        var portal = Css.Declarations(".ts-portal");

        portal["min-height"].ShouldBe("100dvh");
    }

    // ---- target size, 44px ----

    [Theory]
    [InlineData(".ts-site-nav-links a")]
    [InlineData(".ts-breadcrumbs a")]
    [InlineData(".ts-pager a")]
    [InlineData(".ts-next-links a")]
    [InlineData(".ts-product-footer a")]
    [InlineData(".ts-powered a")]
    [InlineData(".ts-error-summary a")]
    [InlineData(".ts-help-link a")]
    [InlineData(".ts-jump-reply")]
    [InlineData(".ts-attachments a")]
    [InlineData(".ts-kb-card h2 a")]
    [InlineData(".ts-product-name")]
    [InlineData(".btn")]
    [InlineData(".form-control")]
    [InlineData(".ts-skip-link")]
    public void A_link_or_control_a_visitor_has_to_hit_is_at_least_44px_tall(string selector)
    {
        Containing(Css, selector)["min-height"].ShouldBe("44px", selector);
    }

    // ---- skip link ----

    [Fact]
    public void The_skip_link_is_off_the_screen_until_it_has_focus_and_does_not_use_the_product_accent()
    {
        var link = Css.OutsideMedia().Declarations(".ts-skip-link");

        link["position"].ShouldBe("absolute");
        link["top"].ShouldBe("-100px");
        link["color"].ShouldBe("var(--p-ink)");
        link["background"].ShouldBe("var(--p-bg)");
        link["border"].ShouldBe("2px solid var(--p-ink)");
        Css.Declarations(".ts-skip-link:focus,.ts-skip-link:focus-visible")["top"].ShouldBe("8px");
        Css.Declarations(".ts-portal-main:focus")["outline"].ShouldBe("none", "main takes focus for the skip link but shows no ring around the whole page");
    }

    // ---- states are never color alone ----

    [Fact]
    public void Errors_use_the_error_tokens_and_a_field_in_error_gets_a_heavier_border_and_text()
    {
        Css.Declarations(".alert-danger.ts-error-summary")["--bs-alert-border-color"].ShouldBe("var(--p-error)");
        Css.Declarations(".alert-danger.ts-error-summary")["--bs-alert-bg"].ShouldBe("var(--p-error-bg)");
        Css.Declarations(".ts-field-error")["color"].ShouldBe("var(--p-error)");
        Css.Declarations(".form-control[aria-invalid=true]")["border-width"].ShouldBe("2px");
        Css.Declarations(".alert-warning")["--bs-alert-border-color"].ShouldBe("var(--p-warn)");
        Css.Declarations(".ts-confirmation")["border"].ShouldBe("2px solid var(--p-success)");
        Css.Text.ShouldNotContain("--p-error,", customMessage: "the old undefined-variable fallback is gone");
        Css.Declarations(".ts-count-over")["text-decoration"].ShouldBe("underline");
    }

    [Fact]
    public void A_form_control_has_an_edge_that_is_not_the_faint_decorative_line()
    {
        Css.Declarations(".form-control,.form-select")["border-color"].ShouldBe("var(--p-ink2)");
    }

    [Fact]
    public void The_counter_is_empty_and_hidden_until_the_script_shows_it()
    {
        Css.Declarations(".ts-char-count[hidden]")["display"].ShouldBe("none");
    }

    // ---- forced colors ----

    [Fact]
    public void Forced_colours_keep_the_3px_focus_outline_and_drop_the_halo()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        var focus = forced.Declarations(":focus-visible,.btn:focus-visible,.form-control:focus,.form-select:focus,.form-check-input:focus");
        focus["outline"].ShouldBe("3px solid Highlight !important");
        focus["box-shadow"].ShouldBe("none !important");
    }

    [Fact]
    public void Forced_colours_keep_what_colour_alone_would_say_with_a_border_a_marker_or_an_underline()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        forced.Declarations(".ts-skip-link")["border"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".ts-error-summary,.ts-confirmation,.alert-warning")["border"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".ts-status-banner,.ts-message-own")["border-left"].ShouldBe("6px solid Highlight");
        forced.Declarations(".form-control[aria-invalid=true]")["border"].ShouldBe("3px solid CanvasText");
        forced.Declarations(".ts-count-over")["border-bottom"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".btn:disabled")["color"].ShouldBe("GrayText");
    }

    // ---- motion ----

    [Fact]
    public void Reduced_motion_switches_every_animation_and_transition_off_and_nothing_scrolls_smoothly()
    {
        var everything = Css.InMedia("(prefers-reduced-motion: reduce)").Declarations("*,*::before,*::after");
        everything["animation"].ShouldBe("none !important");
        everything["transition"].ShouldBe("none !important");

        Css.Text.ShouldNotContain("scroll-behavior", customMessage: "Bootstrap's smooth scrolling is switched off in the build ($enable-smooth-scroll)");
        Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly)
            .Where(file => File.ReadAllText(file).Contains("scroll-behavior", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    // ---- every class has a rule ----

    [GeneratedRegex(@"class=""(?<value>[^""]*)""|className\s*=\s*'(?<js>[^']*)'|ClassList\.Add\(""(?<list>[^""]*)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\bts-[a-z0-9]+(?:-[a-z0-9]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex TsClass();

    // A class of the markup that is deliberately not styled: the accent scope is styled through its custom properties, and the two style-guide wrappers (Development only) only group the samples.
    private static readonly string[] Markers = ["ts-accent-scope", "ts-sg", "ts-sg-form"];

    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        var portal = RepositoryRoot.Combine("src", "TechStrap.Portal");
        foreach (var file in Directory.EnumerateFiles(portal, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            data.Add(Path.GetRelativePath(portal, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Every_ts_class_the_markup_of_a_file_uses_has_a_rule_in_the_compiled_css(string relativePath)
    {
        var source = File.ReadAllText(RepositoryRoot.Combine("src", "TechStrap.Portal", relativePath));
        var used = ClassAttribute().Matches(source)
            .SelectMany(m => new[] { m.Groups["value"].Value, m.Groups["js"].Value, m.Groups["list"].Value })
            .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .SelectMany(token => TsClass().Matches(token).Select(t => t.Value))
            .Where(c => !Markers.Contains(c))
            .Distinct();

        foreach (var name in used)
        {
            Regex.IsMatch(Css.Text, @"\." + Regex.Escape(name) + @"(?![\w-])").ShouldBeTrue($".{name} is used in {relativePath} and has no rule");
        }
    }

    [Fact]
    public void The_class_scan_finds_the_classes_it_is_meant_to_find()
    {
        var found = ClassAttribute().Matches("<div class=\"ts-a ts-b-c btn\"></div> x.className = 'ts-js-one ts-js-two';")
            .SelectMany(m => new[] { m.Groups["value"].Value, m.Groups["js"].Value })
            .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.StartsWith("ts-", StringComparison.Ordinal))
            .ToList();

        found.ShouldBe(["ts-a", "ts-b-c", "ts-js-one", "ts-js-two"]);
    }
}
