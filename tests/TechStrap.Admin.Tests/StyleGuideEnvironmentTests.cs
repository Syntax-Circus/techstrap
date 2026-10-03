using System.Net;

namespace TechStrap.Admin.Tests;

/// <summary>The style guide is a development tool: it exists in Development and is indistinguishable from any unknown page elsewhere.</summary>
public sealed class StyleGuideEnvironmentTests
{
    [Fact]
    public async Task Style_guide_returns_200_in_Development()
    {
        await using var factory = new AdminFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("TechStrap style guide");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Style_guide_returns_404_and_leaks_nothing_outside_Development(string environment)
    {
        await using var factory = new AdminFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldNotContain("TechStrap style guide");
        html.ShouldNotContain("ts-sg-");
    }
}
