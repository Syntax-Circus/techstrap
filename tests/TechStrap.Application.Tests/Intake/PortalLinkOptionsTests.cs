using TechStrap.Application.Intake;

namespace TechStrap.Application.Tests.Intake;

public sealed class PortalLinkOptionsTests
{
    private readonly PortalLinkOptions _options = new() { PublicUrl = "https://help.test/" };

    [Fact]
    public void A_product_host_carries_the_ticket_and_article_links()
    {
        _options.TicketLink("support.dragonpoop.com", "tok").ShouldBe("https://support.dragonpoop.com/t/tok");
        _options.ArticleLink("support.dragonpoop.com", "orbitly", "account", "reset").ShouldBe("https://support.dragonpoop.com/kb/account/reset");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_host_keeps_the_default_host_shapes(string? host)
    {
        _options.TicketLink(host, "tok").ShouldBe("https://help.test/t/tok");
        _options.ArticleLink(host, "orbitly", "account", "reset").ShouldBe("https://help.test/p/orbitly/kb/account/reset");
    }

    [Fact]
    public void The_host_shape_escapes_slugs()
    {
        _options.ArticleLink("support.dragonpoop.com", "orbitly", "a/b", "x?y=1#z").ShouldBe("https://support.dragonpoop.com/kb/a%2Fb/x%3Fy%3D1%23z");
    }

    [Theory]
    [InlineData("https://help.test/p/orbitly/kb/account/reset")]
    [InlineData("https://support.dragonpoop.com/kb/account/reset")]
    public void IsArticleLink_accepts_both_shapes(string url) =>
        PortalLinkOptions.IsArticleLink(url, "orbitly", "account", "reset").ShouldBeTrue();

    [Theory]
    [InlineData("https://help.test/p/other/kb/account/reset")]
    [InlineData("https://support.dragonpoop.com/kb/account/other")]
    [InlineData("https://support.dragonpoop.com/kb/other/reset")]
    public void IsArticleLink_rejects_another_product_or_slug(string url) =>
        PortalLinkOptions.IsArticleLink(url, "orbitly", "account", "reset").ShouldBeFalse();
}
