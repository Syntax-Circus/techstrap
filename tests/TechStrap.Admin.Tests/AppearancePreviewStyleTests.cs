using TechStrap.Contracts.Skins;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

public sealed class AppearancePreviewStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Classic_baseline_resets_every_token_in_the_local_scope()
    {
        var baseline = Css.Declarations(".ts-appearance-preview");
        var classic = SkinResolver.Resolve("classic", null, null).Skin;
        baseline["--p-bg"].ShouldBe(classic.Tokens.Background);
        baseline["--ts-surface"].ShouldBe(classic.Tokens.Surface);
        baseline["--p-ink"].ShouldBe(classic.Tokens.Ink);
        baseline["--p-ink2"].ShouldBe(classic.Tokens.Muted);
        baseline["--p-line"].ShouldBe(classic.Tokens.Border);
        baseline["--ts-accent"].ShouldBe(classic.Tokens.Brand);
        baseline["--ts-on-accent"].ShouldBe(classic.OnBrand);
        baseline["--ts-accent-ink"].ShouldBe(classic.BrandInk);
        baseline["--ts-chrome"].ShouldBe(classic.Tokens.Chrome);
        baseline["--ts-on-chrome"].ShouldBe(classic.OnChrome);
        baseline["--ts-focus"].ShouldBe(classic.Tokens.Focus);
        baseline["--ts-radius"].ShouldBe(".25rem");
        baseline["--ts-border-w"].ShouldBe("1px");
        baseline["color-scheme"].ShouldBe("light");
        baseline["font-family"].ShouldBe("var(--ts-font-body)");
        baseline["background"].ShouldBe("var(--p-bg)");
    }

    [Fact]
    public void Presets_and_control_styles_are_scoped_without_touching_admin_classes()
    {
        Css.Declarations(".ts-appearance-preview[data-ts-shadow=hard] .ts-appearance-preview__card")["box-shadow"].ShouldBe("4px 4px 0 var(--p-ink)");
        Css.Declarations(".ts-appearance-preview[data-ts-shadow=soft] .ts-appearance-preview__card")["box-shadow"].ShouldBe("0 1px 3px rgba(0,0,0,.18)");
        Css.Declarations(".ts-appearance-preview[data-ts-button=outline] .ts-appearance-preview__primary")["color"].ShouldBe("var(--ts-accent-ink)");
        Css.Declarations(".ts-appearance-preview[data-ts-button=bevel] .ts-appearance-preview__primary")["box-shadow"].ShouldContain("inset");
        Css.Declarations(".ts-appearance-preview[data-ts-header=solid] .ts-appearance-preview__header")["background"].ShouldBe("var(--ts-chrome)");
        Css.Declarations(".ts-appearance-preview[data-ts-header=band] .ts-appearance-preview__header")["border-bottom"].ShouldBe("4px solid var(--ts-chrome)");
        var source = File.ReadAllText(RepositoryRoot.Combine("src", "TechStrap.Admin", "Styles", "_appearance-preview.scss"));
        source.ShouldNotContain(".btn");
        source.ShouldNotContain(":root");
        source.ShouldNotContain("[data-bs-theme");
        source.ShouldNotContain("url(");
    }

    [Fact]
    public void All_skin_font_families_are_local_faces_with_real_files()
    {
        foreach (var family in new[] { "IBM Plex Sans", "Nunito", "Atkinson Hyperlegible", "Source Serif 4", "Pixelify Sans" })
        {
            var faces = Css.FontFaces().Where(f => f["font-family"] == family).ToArray();
            faces.ShouldNotBeEmpty();
            foreach (var face in faces)
            {
                face["src"].ShouldNotContain("https:");
            }
        }
    }
}
