using Microsoft.Extensions.Options;
using TechStrap.Application.Products;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

public sealed class ConfiguredProductLogoUrlsTests
{
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/product-logos/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://api.example.com/", "https://api.example.com/product-logos/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://example.com/api", "https://example.com/api/product-logos/0123456789abcdef0123456789abcdef.png")]
    public void A_configured_address_gives_the_absolute_logo_url(string configured, string expected) =>
        new ConfiguredProductLogoUrls(Options.Create(new ProductLogoUrlOptions { PublicUrl = configured })).UrlFor("0123456789abcdef0123456789abcdef.png").ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_address_gives_null_so_emails_keep_the_linked_logo(string configured) =>
        new ConfiguredProductLogoUrls(Options.Create(new ProductLogoUrlOptions { PublicUrl = configured })).UrlFor("0123456789abcdef0123456789abcdef.png").ShouldBeNull();
}
