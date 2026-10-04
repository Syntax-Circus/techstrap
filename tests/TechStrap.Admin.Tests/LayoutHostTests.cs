using TechStrap.Tests.Shared.AdminHost;
using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class LayoutHostTests
{
    [Fact]
    public async Task Home_page_uses_the_layout_with_the_head_mark_and_the_mark_is_served_as_SVG()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);

        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("TechStrap Admin");
        html.ShouldContain("src=\"brand/mark.svg\"");
        html.ShouldContain("ts-shell");
        // The brand link keeps an accessible name when the rail collapses and its text is hidden (Lighthouse link-name).
        html.ShouldContain("class=\"ts-brand\" href=\"/\" aria-label=\"TechStrap Admin home\"");

        var mark = await client.GetAsync("/brand/mark.svg", TestContext.Current.CancellationToken);
        mark.StatusCode.ShouldBe(HttpStatusCode.OK);
        mark.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
    }
}
