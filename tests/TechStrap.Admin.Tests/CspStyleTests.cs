using System.Text.RegularExpressions;
using TechStrap.Hosting.Security;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Review Focus 1, the style half: the policy lets the stylesheet draw everything it draws. The compiled CSS may reference only same-origin files (fonts, images) and
/// <c>data:</c> images, and the policy must allow exactly those, or a colour, a font or an icon silently disappears in a browser while every other test is green.
/// </summary>
public sealed partial class CspStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<target>.*?)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlReference();

    [GeneratedRegex(@"@import\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ImportRule();

    private static string[] Sources(string directive) =>
        TechStrapCsp.ForBlazorApp().Split(';', StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' '))
            .Single(parts => parts[0] == directive)[1..];

    private static IReadOnlyList<string> Targets() => [.. UrlReference().Matches(Css.Text).Select(m => m.Groups["target"].Value)];

    [Fact]
    public void The_stylesheet_references_fonts_and_images_so_this_check_scans_something()
    {
        Targets().ShouldContain(t => t.EndsWith(".woff2", StringComparison.Ordinal), "the self-hosted fonts");
        Targets().ShouldContain(t => t.StartsWith("data:image/", StringComparison.Ordinal), "Bootstrap's inline SVG icons");
    }

    [Fact]
    public void The_stylesheet_loads_nothing_from_another_host_and_imports_nothing()
    {
        Targets().ShouldAllBe(t => !t.StartsWith("http:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("//", StringComparison.Ordinal));
        ImportRule().IsMatch(Css.Text).ShouldBeFalse("style-src 'self' would block a remote stylesheet, and an @import hides one from this check");
    }

    [Fact]
    public void The_policy_allows_every_kind_of_url_the_stylesheet_uses()
    {
        var targets = Targets();
        if (targets.Any(t => t.StartsWith("data:image/", StringComparison.Ordinal)))
        {
            Sources("img-src").ShouldContain("data:");
        }

        if (targets.Any(t => t.EndsWith(".woff2", StringComparison.Ordinal)))
        {
            Sources("font-src").ShouldBe(["'self'"]);
        }

        targets.Where(t => !t.StartsWith("data:", StringComparison.Ordinal) && !t.EndsWith(".woff2", StringComparison.Ordinal))
            .ShouldBeEmpty("a url() of a kind the policy was not written for: decide which directive covers it");
    }
}
