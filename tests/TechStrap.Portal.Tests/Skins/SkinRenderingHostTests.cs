using System.Text.RegularExpressions;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Settings;

namespace TechStrap.Portal.Tests.Skins;

/// <summary>D-053 at the host: a resolved skin reaches the page only as validated custom properties and preset attributes on the one style carrier, and a page with no skin is byte for byte what it was.</summary>
public sealed class SkinRenderingHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Paperplane() =>
        new("paperplane", "Paperplane", "https://cdn.example.com/paperplane.png", "#F59E0B", "#000000", "#9D6507");

    // The asset fingerprints (css/app.<hash>.css, blazor.web.<hash>.js, ...) change with every build: the golden holds them without the hash.
    private static string Normalise(string html) =>
        Regex.Replace(html.Replace("\r\n", "\n", StringComparison.Ordinal), @"\.[a-z0-9]{10}\.(css|js)\b", ".$1");

    [Fact]
    public async Task An_unskinned_product_page_is_byte_identical()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        using var client = factory.CreateClient();

        var html = Normalise(await client.GetStringAsync("/p/paperplane", Ct));

        html.ShouldBe(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unskinned-product-home.html")).Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
