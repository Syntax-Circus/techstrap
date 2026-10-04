using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class ScriptHostTests
{
    [Theory]
    [InlineData("/js/dialog.js")]
    [InlineData("/js/shortcuts.js")]
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("export ");
    }
}
