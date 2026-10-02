using System.Net;

namespace TechStrap.Api.Tests;

/// <summary>
/// The Admin and Portal placeholder shells serve one static page, the brand icons, and the CSS that
/// the build compiles from Styles/app.scss (compiled CSS is never committed).
/// </summary>
public sealed class ShellHostTests
{
    private static readonly string[] IconContentTypes = ["image/x-icon", "image/vnd.microsoft.icon"];

    [Fact]
    public async Task Admin_serves_the_placeholder_page_icons_and_compiled_css()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        await AssertShellAsync(client, "TechStrap Admin");
    }

    [Fact]
    public async Task Portal_serves_the_placeholder_page_icons_and_compiled_css()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        await AssertShellAsync(client, "TechStrap Portal");
    }

    private static async Task AssertShellAsync(HttpClient client, string expectedHeading)
    {
        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain(expectedHeading);
        html.ShouldContain("rel=\"icon\" href=\"favicon.ico\" sizes=\"any\"");
        html.ShouldContain("rel=\"apple-touch-icon\" href=\"apple-touch-icon.png\"");

        var favicon = await client.GetAsync("/favicon.ico", TestContext.Current.CancellationToken);
        favicon.StatusCode.ShouldBe(HttpStatusCode.OK);
        IconContentTypes.ShouldContain(favicon.Content.Headers.ContentType?.MediaType);

        foreach (var path in new[] { "/favicon-32.png", "/apple-touch-icon.png", "/icon-192.png", "/icon-512.png" })
        {
            (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        var css = await client.GetAsync("/css/app.css", TestContext.Current.CancellationToken);
        css.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await css.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(".shell-placeholder");

        var script = await client.GetAsync("/_framework/blazor.web.js", TestContext.Current.CancellationToken);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
