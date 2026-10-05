using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed partial class ScriptHostTests
{
    [GeneratedRegex(@"<script\b[^>]*theme-init[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThemeScriptTag();

    [Theory]
    [InlineData("/js/dialog.js")]
    [InlineData("/js/shortcuts.js")]
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/js/tz.js")]
    [InlineData("/js/palette.js")]
    [InlineData("/js/menu.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("export ");
    }

    // The theme script is not a module: a module is deferred and would run after the first paint, which is the flash it exists to prevent.
    [Fact]
    public async Task The_theme_script_is_served_without_signing_in_and_is_a_classic_script()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/js/theme-init.js", TestContext.Current.CancellationToken);
        var script = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
        script.ShouldContain("techstrap.admin.theme");
        script.ShouldNotContain("export ");
        script.ShouldNotContain("import ");
    }

    [Theory]
    [InlineData("/signin", false)]
    [InlineData("/error", false)]
    [InlineData("/", true)]
    public async Task Every_page_loads_the_theme_script_in_head_before_the_styles_as_a_blocking_classic_script(string path, bool signedIn)
    {
        await using var factory = new AdminFactory();
        using var client = signedIn ? factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent) : factory.CreateClient();

        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);

        var head = html[..html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase)];
        var tag = ThemeScriptTag().Match(head);
        tag.Success.ShouldBeTrue("the theme script must be in <head>");
        tag.Value.ShouldNotContain("defer", Case.Insensitive);
        tag.Value.ShouldNotContain("async", Case.Insensitive);
        tag.Value.ShouldNotContain("module", Case.Insensitive);
        tag.Value.ShouldNotContain("type=", Case.Insensitive);
        head.IndexOf(tag.Value, StringComparison.Ordinal).ShouldBeLessThan(head.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal), "before the first stylesheet, so the theme is set before the first paint");
        if (signedIn)
        {
            factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        }
    }
}
