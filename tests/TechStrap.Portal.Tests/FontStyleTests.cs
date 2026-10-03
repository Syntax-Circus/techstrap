using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>Fonts are self-hosted (BRAND.md section 11): no CDN, font-display swap, and a system fallback in every stack. The portal uses Plex Sans, plus Plex Mono for the ticket id only.</summary>
public sealed class FontStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [Fact]
    public void Portal_ships_only_Plex_Sans_and_Plex_Mono_from_its_own_wwwroot()
    {
        FontStyleRules.EveryFontFaceIsSelfHostedAndSwaps(Css, "TechStrap.Portal", "IBM Plex Mono", "IBM Plex Sans");
    }

    [Fact]
    public void Every_font_stack_ends_with_a_system_fallback()
    {
        var root = Css.Declarations(":root,[data-bs-theme=light]");

        root["--ts-font-sans"].ShouldStartWith("\"IBM Plex Sans\"");
        root["--ts-font-sans"].ShouldEndWith("sans-serif");
        root["--ts-font-mono"].ShouldStartWith("\"IBM Plex Mono\"");
        root["--ts-font-mono"].ShouldEndWith("monospace");
        root["--bs-font-sans-serif"].ShouldStartWith("\"IBM Plex Sans\"");
    }

    [Fact]
    public void The_stylesheet_calls_no_third_party_host()
    {
        FontStyleRules.NoCssReferencesAThirdPartyHost(Css);
    }

    [Fact]
    public void The_OFL_licence_text_ships_beside_every_family()
    {
        FontStyleRules.LicencesShipBesideTheFonts("TechStrap.Portal", "ibm-plex-sans", "ibm-plex-mono");
    }
}
