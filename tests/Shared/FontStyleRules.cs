using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>
/// The font rules of docs/BRAND.md section 11 that both apps share: self-hosted, swap, and a system fallback at the end of
/// every stack so text stays readable when a font file fails to load. Each app passes the families it ships.
/// </summary>
internal static partial class FontStyleRules
{
    [GeneratedRegex(@"url\(""?(?<path>[^)""]+)""?\)")]
    private static partial Regex UrlPattern();

    public static void EveryFontFaceIsSelfHostedAndSwaps(CompiledCss css, string app, params string[] expectedFamilies)
    {
        var faces = css.FontFaces();

        faces.Select(f => f["font-family"]).Distinct().Order().ShouldBe(expectedFamilies.Order());
        var cssDirectory = RepositoryRoot.Combine("src", app, "wwwroot", "css");
        foreach (var face in faces)
        {
            face["font-display"].ShouldBe("swap", face["font-family"]);
            var path = UrlPattern().Match(face["src"]).Groups["path"].Value;
            path.ShouldStartWith("../fonts/", Case.Sensitive, $"{face["font-family"]} must be self-hosted");
            File.Exists(Path.GetFullPath(Path.Combine(cssDirectory, path))).ShouldBeTrue($"{path} should be restored by libman");
        }
    }

    /// <summary>Every url() in the stylesheet is an inline data: URI (Bootstrap's SVG icons) or one of our own ../fonts files.</summary>
    public static void NoCssReferencesAThirdPartyHost(CompiledCss css)
    {
        foreach (Match url in UrlPattern().Matches(css.Text))
        {
            var path = url.Groups["path"].Value;
            (path.StartsWith("data:", StringComparison.Ordinal) || path.StartsWith("../fonts/", StringComparison.Ordinal))
                .ShouldBeTrue($"url({path}) must not leave the app");
        }

        css.Text.ShouldNotContain("@import url", Case.Insensitive);
        css.Text.ShouldNotContain("googleapis", Case.Insensitive);
        css.Text.ShouldNotContain("gstatic", Case.Insensitive);
    }

    public static void LicencesShipBesideTheFonts(string app, params string[] directories)
    {
        foreach (var directory in directories)
        {
            var licence = RepositoryRoot.Combine("src", app, "wwwroot", "fonts", directory, "LICENSE");
            File.Exists(licence).ShouldBeTrue($"{licence} should be restored by libman");
            File.ReadAllText(licence).ShouldContain("SIL Open Font License");
        }
    }
}
