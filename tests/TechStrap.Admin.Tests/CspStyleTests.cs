using System.Text.RegularExpressions;
using TechStrap.Hosting.Security;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Review Focus 1, the style half: the policy lets the stylesheet draw everything it draws. The compiled CSS of both Blazor hosts may reference only same-origin fonts and
/// <c>data:</c> images, and the policy must allow exactly those, or a color, a font or an icon silently disappears in a browser while every other test is green.
/// </summary>
public sealed partial class CspStyleTests
{
    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<target>.*?)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlReference();

    [GeneratedRegex(@"@import\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ImportRule();

    public static TheoryData<string> Apps => ["TechStrap.Admin", "TechStrap.Portal"];

    private static string[] Sources(string directive) =>
        TechStrapCsp.ForBlazorApp().Split(';', StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' '))
            .Single(parts => parts[0] == directive)[1..];

    private static IReadOnlyList<string> Targets(string css) => [.. UrlReference().Matches(css).Select(m => m.Groups["target"].Value)];

    private static bool IsDataImage(string target) => target.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsFont(string target) => target.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the policy does not cover in a stylesheet: another host, an @import, a data: URL that is not an image (font-src 'self' blocks a data: font), any other kind of url().</summary>
    private static IReadOnlyList<string> Uncovered(string css)
    {
        var problems = new List<string>();
        problems.AddRange(Targets(css)
            .Where(t => !IsDataImage(t) && !IsFont(t))
            .Select(t => $"url({t}) is not a same-origin .woff2 font or a data:image/ image"));
        if (ImportRule().IsMatch(css))
        {
            problems.Add("@import hides a stylesheet from this check, and style-src 'self' would block a remote one");
        }

        return problems;
    }

    [Theory]
    [InlineData("a{src:url(data:font/woff2;base64,AAAA)}")]
    [InlineData("a{src:url('data:application/font-woff;base64,AAAA')}")]
    [InlineData("a{background:url(https://cdn.example.test/a.png)}")]
    [InlineData("a{background:url(//cdn.example.test/a.png)}")]
    [InlineData("a{background:url(http://cdn.example.test/a.png)}")]
    [InlineData("a{background:url(a.png)}")]
    [InlineData("@import url(x.css);")]
    [InlineData("@import 'x.css';")]
    public void A_url_the_policy_does_not_cover_is_reported(string css)
    {
        Uncovered(css).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("a{src:url(../fonts/a.woff2)}")]
    [InlineData("a{background:url(\"data:image/svg+xml,%3csvg%3e\")}")]
    [InlineData("a{background:url('data:image/png;base64,AAAA')}")]
    public void A_same_origin_font_and_a_data_image_are_covered(string css)
    {
        Uncovered(css).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void The_stylesheet_references_fonts_and_images_so_this_check_scans_something(string app)
    {
        var targets = Targets(CompiledCss.Load(app).Text);

        targets.ShouldContain(t => IsFont(t), "the self-hosted fonts");
        targets.ShouldContain(t => IsDataImage(t), "Bootstrap's inline SVG icons");
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void The_stylesheet_loads_nothing_from_another_host_and_imports_nothing(string app)
    {
        Uncovered(CompiledCss.Load(app).Text).ShouldBeEmpty();
        Targets(CompiledCss.Load(app).Text).ShouldAllBe(t => !t.StartsWith("http:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("//", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void The_policy_allows_every_kind_of_url_the_stylesheet_uses(string app)
    {
        var targets = Targets(CompiledCss.Load(app).Text);
        if (targets.Any(IsDataImage))
        {
            Sources("img-src").ShouldContain("data:");
        }

        if (targets.Any(IsFont))
        {
            Sources("font-src").ShouldBe(["'self'"]);
        }

        Uncovered(CompiledCss.Load(app).Text).ShouldBeEmpty("a url() of a kind the policy was not written for: decide which directive covers it");
    }
}
