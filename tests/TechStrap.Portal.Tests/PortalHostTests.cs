using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

public sealed class PortalHostTests
{
    [Fact]
    public async Task Style_guide_returns_200_in_Development_with_the_footer_and_every_sample_accent()
    {
        await using var factory = new PortalFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("TechStrap portal style guide");
        html.ShouldContain("ts-powered");
        html.ShouldContain("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        html.ShouldContain("--ts-accent:#4B7D87;--ts-on-accent:#FFFFFF;--ts-accent-ink:#4B7D87");
        html.ShouldNotContain("ts-window");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Style_guide_returns_404_and_leaks_nothing_outside_Development(string environment)
    {
        await using var factory = new PortalFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("TechStrap portal style guide");
        html.ShouldNotContain("ts-sg-");
    }

    [Fact]
    public async Task The_setting_TECHSTRAP_PORTAL_SHOW_POWERED_BY_false_hides_the_footer_on_every_page()
    {
        await using var factory = new PortalFactory().WithWebHostBuilder(b => b.UseSetting("TECHSTRAP_PORTAL_SHOW_POWERED_BY", "false"));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldNotContain("ts-powered");
    }

    [Fact]
    public async Task A_malformed_TECHSTRAP_PORTAL_SHOW_POWERED_BY_fails_at_startup_instead_of_on_every_page()
    {
        await using var root = new PortalFactory();
        await using var factory = root.WithWebHostBuilder(b => b.UseSetting("TECHSTRAP_PORTAL_SHOW_POWERED_BY", "maybe"));

        var failure = StartupFailure.Capture(factory, () => root.LogSink.Events);

        failure.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
    }

    [Fact]
    public async Task The_footer_shows_by_default_on_the_home_page()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldContain("ts-powered");
        html.ShouldContain("<h1>Support</h1>");
    }
}
