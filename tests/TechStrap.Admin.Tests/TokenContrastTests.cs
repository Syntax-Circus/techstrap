using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// BRAND.md section 12 states that every text-on-background pair meets WCAG AA (4.5:1) in both themes. These tests pin that
/// claim against the compiled CSS, so a token edit that breaks a pair fails the build. One pair is deliberately absent:
/// --ink-3 on --sel in the dark theme is 4.32:1, so tertiary text must not be placed on a selected row (BRAND.md section 12 rule).
/// </summary>
public sealed class TokenContrastTests
{
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    private static readonly (string Foreground, string Background)[] Pairs =
    [
        ("ink", "paper"), ("ink", "sheet"), ("ink", "rail"), ("ink", "head"), ("ink", "hover"), ("ink", "sel"), ("ink", "canary"), ("ink", "pink"),
        ("ink-2", "paper"), ("ink-2", "sheet"), ("ink-2", "rail"), ("ink-2", "head"), ("ink-2", "hover"), ("ink-2", "sel"), ("ink-2", "canary"), ("ink-2", "pink"),
        ("ink-3", "paper"), ("ink-3", "sheet"), ("ink-3", "rail"), ("ink-3", "head"), ("ink-3", "hover"), ("ink-3", "canary"), ("ink-3", "pink"),
        ("note-ink", "pink"), ("note-ink", "sheet"), ("note-ink", "paper"), ("note-ink", "canary"),
        ("st-new", "paper"), ("st-new", "sheet"), ("st-new", "hover"), ("st-new", "sel"),
        ("st-open", "paper"), ("st-open", "sheet"), ("st-open", "hover"), ("st-open", "sel"),
        ("st-pending", "paper"), ("st-pending", "sheet"), ("st-pending", "hover"), ("st-pending", "sel"),
        ("st-solved", "paper"), ("st-solved", "sheet"), ("st-solved", "hover"), ("st-solved", "sel"),
        ("st-closed", "paper"), ("st-closed", "sheet"), ("st-closed", "hover"), ("st-closed", "sel"),
        ("st-spam", "paper"), ("st-spam", "sheet"), ("st-spam", "hover"), ("st-spam", "sel"),
        ("on-accent", "accent"), ("accent", "paper"), ("accent", "sheet"), ("accent", "head"),
        ("bm-text", "bm-plate"), ("bm-text2", "bm-plate"), ("bm-on-bar", "bm-bar"), ("bm-on-crt", "bm-crt"),
    ];

    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            foreach (var (foreground, background) in Pairs)
            {
                data.Add(theme, foreground, background);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Text_pair_meets_AA(string theme, string foreground, string background)
    {
        var scope = theme == "light" ? ":root,[data-bs-theme=light]" : "[data-bs-theme=dark]";
        var tokens = Css.Declarations(scope);

        ProductAccent.ContrastRatio(tokens[$"--{foreground}"], tokens[$"--{background}"])
            .ShouldBeGreaterThanOrEqualTo(Aa, $"--{foreground} on --{background} in the {theme} theme");
    }
}
