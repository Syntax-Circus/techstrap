using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

/// <summary>Review Focus 4: the logo becomes an image source in customer emails and on the portal, so only a safe absolute URL may be stored.</summary>
public sealed class ProductBrandingLogoTests
{
    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/logo.png")]
    [InlineData("  https://cdn.orbitly.example/a/b.svg?v=2  ", "https://cdn.orbitly.example/a/b.svg?v=2")]
    [InlineData("HTTPS://CDN.ORBITLY.EXAMPLE/Logo.PNG", "https://cdn.orbitly.example/Logo.PNG")]
    [InlineData("https://cdn.orbitly.example", "https://cdn.orbitly.example/")]
    [InlineData("https://cdn.orbitly.example/a\"b<c>d.png", "https://cdn.orbitly.example/a%22b%3Cc%3Ed.png")]
    [InlineData("http://localhost/logo.png", "http://localhost/logo.png")]
    [InlineData("http://localhost:5080/logo.png", "http://localhost:5080/logo.png")]
    [InlineData("http://127.0.0.1:8080/logo.png", "http://127.0.0.1:8080/logo.png")]
    public void A_safe_absolute_logo_url_is_kept_trimmed(string input, string expected)
    {
        var stored = ProductBranding.Create("Orbitly", input, null, null, null).Value.LogoPath;

        stored.ShouldBe(expected);
        // The email renderer shows a logo only when it starts with "https://", so every accepted https sample must be stored in that form.
        if (input.Contains("https", StringComparison.OrdinalIgnoreCase))
        {
            stored.ShouldStartWith("https://");
        }
    }

    private static ProductBranding StoredWith(string? logo) => ProductBranding.Restore("Orbitly", logo, "#1F6FEB", null, null);

    [Theory]
    [InlineData("/images/old-logo.png")]
    [InlineData("//cdn.orbitly.example/logo.png")]
    [InlineData("http://cdn.orbitly.example/logo.png")]
    public void An_update_that_leaves_a_stored_logo_unchanged_accepts_it_even_when_the_rule_would_refuse_it(string stored)
    {
        var result = ProductBranding.CreateForUpdate(StoredWith(stored), "Orbitly Cloud", stored, "#7C3AED", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LogoPath.ShouldBe(stored);
        result.Value.DisplayName.ShouldBe("Orbitly Cloud");
        result.Value.AccentColour.ShouldBe("#7C3AED");
    }

    [Fact]
    public void The_unchanged_logo_is_compared_after_trimming()
    {
        var result = ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", "  /images/old-logo.png  ", null, null, null);

        result.Value.LogoPath.ShouldBe("/images/old-logo.png");
    }

    [Theory]
    [InlineData("/images/other.png")]
    [InlineData("/IMAGES/OLD-LOGO.PNG")]
    [InlineData("javascript:alert(1)")]
    public void A_different_logo_is_checked_by_the_full_rule_even_when_the_product_has_a_stored_one(string requested)
    {
        var result = ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", requested, null, null, null);

        result.Error!.Code.ShouldBe("logo-path-invalid");
    }

    [Fact]
    public void A_product_with_no_stored_logo_gets_no_exception()
    {
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", "/images/logo.png", null, null, null).Error!.Code.ShouldBe("logo-path-invalid");
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", null, null, null, null).Value.LogoPath.ShouldBeNull();
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", "https://cdn.orbitly.example/logo.png", null, null, null).Value.LogoPath.ShouldBe("https://cdn.orbitly.example/logo.png");
    }

    [Fact]
    public void Clearing_a_stored_relative_logo_is_accepted_and_every_other_field_is_still_checked_when_the_logo_is_left_alone()
    {
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", null, null, null, null).Value.LogoPath.ShouldBeNull();
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", "/images/old-logo.png", "purple", null, null).Error!.Code.ShouldBe("accent-colour-invalid");
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "  ", "/images/old-logo.png", null, null, null).Error!.Code.ShouldBe("display-name-required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_logo_is_allowed_and_stored_as_null(string? input)
    {
        ProductBranding.Create("Orbitly", input, null, null, null).Value.LogoPath.ShouldBeNull();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://cdn.orbitly.example/logo.png")]
    [InlineData("/logo.svg")]
    [InlineData("logo.svg")]
    [InlineData("../logo.svg")]
    [InlineData("//cdn.orbitly.example/logo.png")]
    [InlineData("http://cdn.orbitly.example/logo.png")]
    [InlineData("http://localhost.evil.example/logo.png")]
    [InlineData("http://192.168.1.10/logo.png")]
    [InlineData("https://")]
    [InlineData("https:///logo.png")]
    [InlineData("https://user:secret@cdn.orbitly.example/logo.png")]
    [InlineData("https://cdn.orbitly.example/lo go.png")]
    [InlineData("https://cdn.orbitly.example/lo\ngo.png")]
    [InlineData("https://cdn.orbitly.example/\u0001logo.png")]
    [InlineData("https://cdn.orbitly.example/a\u200Bb.png")]
    [InlineData("https://cdn.orbitly.example/a\u202Eb.png")]
    public void An_unsafe_or_relative_logo_is_rejected_with_a_logo_path_error(string input)
    {
        var error = ProductBranding.Create("Orbitly", input, null, null, null).Error!;

        error.Kind.ShouldBe(DomainErrorKind.Validation);
        error.Code.ShouldBe("logo-path-invalid");
        error.Target.ShouldBe("logo-path");
    }

    [Fact]
    public void A_logo_longer_than_the_limit_is_too_long_not_invalid()
    {
        var url = "https://cdn.orbitly.example/" + new string('a', 500);

        ProductBranding.Create("Orbitly", url, null, null, null).Error!.Code.ShouldBe("logo-path-too-long");
    }

    [Fact]
    public void A_logo_of_exactly_the_limit_is_accepted()
    {
        var url = "https://cdn.orbitly.example/" + new string('a', 500 - "https://cdn.orbitly.example/".Length);

        url.Length.ShouldBe(500);
        ProductBranding.Create("Orbitly", url, null, null, null).Value.LogoPath.ShouldBe(url);
    }

    [Fact]
    public void Restore_does_not_revalidate_a_stored_logo_so_old_rows_still_load()
    {
        ProductBranding.Restore("Orbitly", "/logo.svg", "#1F6FEB", null, null).LogoPath.ShouldBe("/logo.svg");
    }
}
