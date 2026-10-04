using TechStrap.Tests.Shared.AdminHost;
using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class NotFoundHostTests
{
    [Fact]
    public async Task An_unknown_address_returns_404_with_the_retro_window_and_a_way_back()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var response = await client.GetAsync("/no-such-page", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("ts-window");
        html.ShouldContain("ERROR 404");
        html.ShouldContain("This page fell out of its strap.");
        html.ShouldContain("Back to the queue");
        html.ShouldContain("ts-shell");
    }
}
