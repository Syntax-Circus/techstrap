using TechStrap.Admin.Options;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests.Options;

/// <summary>The optional portal address behind the "View on portal" link: blank is fine, anything else must be a plain http or https address, and the article address is built from escaped parts.</summary>
public sealed class PortalUrlOptionsTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("https://help.example.com", true)]
    [InlineData("https://help.example.com/", true)]
    [InlineData("http://localhost:8082", true)]
    [InlineData("https://example.com/portal", true)]
    [InlineData("help.example.com", false)]
    [InlineData("/p/orbitly", false)]
    [InlineData("ftp://help.example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://help.example.com?x=1", false)]
    [InlineData("https://help.example.com?", false)]
    [InlineData("https://help.example.com#top", false)]
    [InlineData("https://help.example.com#", false)]
    [InlineData("https://user:pass@help.example.com", false)]
    public void The_portal_address_is_blank_or_a_plain_absolute_http_or_https_address(string? value, bool valid) =>
        PortalUrlOptions.IsValidBase(value).ShouldBe(valid);

    [Fact]
    public void An_article_address_follows_the_portal_route_and_a_trailing_slash_makes_no_difference()
    {
        new PortalUrlOptions { PublicUrl = "https://help.example.com/" }.ArticleUrl("orbitly", "account", "reset-password")
            .ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        new PortalUrlOptions { PublicUrl = " https://help.example.com/portal " }.ArticleUrl("orbitly", "account", "reset-password")
            .ShouldBe("https://help.example.com/portal/p/orbitly/kb/account/reset-password");
    }

    [Fact]
    public void Each_part_is_escaped_so_a_slug_can_never_add_a_segment_or_a_query()
    {
        new PortalUrlOptions { PublicUrl = "https://help.example.com" }.ArticleUrl("or bit", "a/b", "x?y=1#z")
            .ShouldBe("https://help.example.com/p/or%20bit/kb/a%2Fb/x%3Fy%3D1%23z");
    }

    [Theory]
    [InlineData(null, "orbitly", "account", "slug")]
    [InlineData("", "orbitly", "account", "slug")]
    [InlineData("https://help.example.com", null, "account", "slug")]
    [InlineData("https://help.example.com", "orbitly", "", "slug")]
    [InlineData("https://help.example.com", "orbitly", "account", " ")]
    public void With_no_portal_address_or_a_missing_part_there_is_no_link(string? portal, string? product, string? category, string? slug) =>
        new PortalUrlOptions { PublicUrl = portal }.ArticleUrl(product, category, slug).ShouldBeNull();

    [Fact]
    public async Task A_blank_portal_address_lets_the_host_start()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = "" });

        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("help.example.com")]
    [InlineData("https://help.example.com?x=1")]
    public async Task A_portal_address_that_is_not_a_plain_absolute_address_stops_the_start_and_names_the_variable(string value)
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = value });

        StartupFailure.Capture(factory, () => factory.LogSink.Events).Message.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
    }
}
