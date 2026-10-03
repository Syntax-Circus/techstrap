using System.Net;

namespace TechStrap.Portal.Tests;

public sealed class NotFoundHostTests
{
    [Fact]
    public async Task An_unknown_address_returns_404_with_the_plain_portal_not_found_page()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/nope", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Page not found");
        html.ShouldContain("Back to the start");
        html.ShouldNotContain("ts-window");
    }
}
