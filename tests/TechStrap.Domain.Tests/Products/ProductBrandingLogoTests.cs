using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

/// <summary>Review Focus 4: the logo becomes an image source in customer emails and on the portal, so only a safe absolute URL may be stored.</summary>
public sealed class ProductBrandingLogoTests
{
    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/logo.png")]
    [InlineData("  https://cdn.orbitly.example/a/b.svg?v=2  ", "https://cdn.orbitly.example/a/b.svg?v=2")]
    [InlineData("HTTPS://CDN.ORBITLY.EXAMPLE/Logo.PNG", "HTTPS://CDN.ORBITLY.EXAMPLE/Logo.PNG")]
    [InlineData("http://localhost/logo.png", "http://localhost/logo.png")]
    [InlineData("http://localhost:5080/logo.png", "http://localhost:5080/logo.png")]
    [InlineData("http://127.0.0.1:8080/logo.png", "http://127.0.0.1:8080/logo.png")]
    public void A_safe_absolute_logo_url_is_kept_trimmed(string input, string expected)
    {
        ProductBranding.Create("Orbitly", input, null, null, null).Value.LogoPath.ShouldBe(expected);
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
