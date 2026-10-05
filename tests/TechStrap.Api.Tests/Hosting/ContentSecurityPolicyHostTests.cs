using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// Review Focus 1: the policy each web host really sends, read back from the response, directive by directive (never computed from the code that builds it). A policy
/// that is weaker than decided, or that breaks sign-in or the page, fails here; the owner's browser check (ADMIN-APP.md) covers what only a browser can show.
/// </summary>
/// <remarks>
/// Program.cs reads <c>Auth:Authority</c> while it builds the host (before the factory's in-memory settings exist), so the Admin's authority reaches it as the
/// <c>Auth__Authority</c> process environment variable. That variable is set per test here and restored on dispose, never in a static constructor: the class runs in the
/// non-parallel <see cref="ProcessEnvironmentCollection"/>, so no other test's host ever sees it.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed partial class ContentSecurityPolicyHostTests : IDisposable
{
    private const string AuthorityVariable = "Auth__Authority";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly string? _previousAuthority = Environment.GetEnvironmentVariable(AuthorityVariable);

    public ContentSecurityPolicyHostTests() => Environment.SetEnvironmentVariable(AuthorityVariable, AdminTestSettings.Authority);

    public void Dispose() => Environment.SetEnvironmentVariable(AuthorityVariable, _previousAuthority);

    [GeneratedRegex(@"<script\b(?![^>]*(?<![\w-])src\s*=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptTag();

    [GeneratedRegex(@"<style\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleElement();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerAttribute();

    private static Dictionary<string, string[]> Policy(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Content-Security-Policy", out var values).ShouldBeTrue("the response has no Content-Security-Policy");
        var header = values!.Single();
        return header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..], StringComparer.Ordinal);
    }

    private static void AssertBrowserPolicy(Dictionary<string, string[]> policy, string[] formAction, bool loopbackImages)
    {
        policy["default-src"].ShouldBe(["'self'"]);
        policy["script-src"].ShouldBe(["'self'"]);
        policy["style-src"].ShouldBe(["'self'"]);
        policy["style-src-attr"].ShouldBe(["'unsafe-inline'"]);
        policy["connect-src"].ShouldBe(["'self'"]);
        policy["font-src"].ShouldBe(["'self'"]);
        policy["object-src"].ShouldBe(["'none'"]);
        policy["frame-ancestors"].ShouldBe(["'none'"]);
        policy["base-uri"].ShouldBe(["'self'"]);
        policy["form-action"].ShouldBe(formAction);
        policy["img-src"].Take(3).ShouldBe(["'self'", "https:", "data:"]);
        policy["img-src"].Skip(3).ShouldBe(loopbackImages ? ["http://localhost:*", "http://127.0.0.1:*"] : []);

        // Nothing weaker than decided: script never allows inline code or eval, and 'unsafe-inline' appears in exactly one directive.
        policy.Keys.ShouldNotContain("script-src-attr");
        policy.Where(d => d.Value.Contains("'unsafe-inline'")).Select(d => d.Key).ShouldBe(["style-src-attr"]);
        policy.Values.SelectMany(v => v).ShouldNotContain("'unsafe-eval'");
        policy.Values.SelectMany(v => v).ShouldNotContain("*");
        policy.Values.SelectMany(v => v).ShouldNotContain("http:");
    }

    [Fact]
    public async Task The_Admin_pages_carry_the_decided_policy_with_the_identity_provider_in_form_action()
    {
        await using var factory = new AdminFactory();
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        var idp = new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority);

        using var signIn = await anonymous.GetAsync("/signin", Ct);
        using var error = await anonymous.GetAsync("/error", Ct);
        using var home = await signedIn.GetAsync("/", Ct);
        using var missing = await signedIn.GetAsync("/no-such-page", Ct);

        foreach (var response in new[] { signIn, error, home, missing })
        {
            AssertBrowserPolicy(Policy(response), ["'self'", idp], loopbackImages: true);
        }

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task In_Production_the_Admin_does_not_allow_loopback_logos()
    {
        // Production refuses to start without a trusted proxy network, which Program.cs binds before the factory's settings exist.
        Environment.SetEnvironmentVariable("TrustedProxy__TrustedNetworks__0", "192.0.2.0/24");
        try
        {
            await using var factory = new AdminFactory("Production");
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/signin", Ct);

            AssertBrowserPolicy(Policy(response), ["'self'", new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority)], loopbackImages: false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TrustedProxy__TrustedNetworks__0", null);
        }
    }

    [Fact]
    public async Task The_form_action_origin_follows_the_configured_authority()
    {
        // Dispose restores the previous value, so this override cannot outlive the test.
        Environment.SetEnvironmentVariable(AuthorityVariable, "https://sso.example.test:8443/application/o/techstrap-admin/");
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = "https://sso.example.test:8443/application/o/techstrap-admin/" });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/signin", Ct);

        Policy(response)["form-action"].ShouldBe(["'self'", "https://sso.example.test:8443"]);
    }

    [Fact]
    public async Task The_Portal_carries_the_decided_policy_with_no_identity_provider()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var home = await client.GetAsync("/", Ct);
        using var missing = await client.GetAsync("/no-such-page", Ct);

        AssertBrowserPolicy(Policy(home), ["'self'"], loopbackImages: true);
        AssertBrowserPolicy(Policy(missing), ["'self'"], loopbackImages: true);
    }

    [Fact]
    public async Task The_Api_policy_allows_nothing()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health/live", Ct);
        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);

        foreach (var response in new[] { health, openApi })
        {
            var policy = Policy(response);
            policy["default-src"].ShouldBe(["'none'"]);
            policy["frame-ancestors"].ShouldBe(["'none'"]);
            policy["base-uri"].ShouldBe(["'none'"]);
            policy["form-action"].ShouldBe(["'none'"]);
        }
    }

    // The policy forbids inline script and event-handler attributes. Every page the hosts render must already obey it: a page that renders an inline <script> (the import map
    // component does) would work in a test and be blocked in a browser, so the rendered HTML is checked, not only the header.
    [Fact]
    public async Task No_Admin_page_renders_an_inline_script_a_style_element_or_an_event_handler_attribute()
    {
        await using var factory = new AdminFactory();
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // The sign-in page is a standalone static page; the others are rendered by App.razor and load the framework script, which proves the scan sees a real page.
        var pages = new List<(string Html, bool Framework)>
        {
            (await anonymous.GetStringAsync("/signin", Ct), false),
            (await anonymous.GetStringAsync("/error", Ct), true),
            (await anonymous.GetStringAsync("/_styleguide", Ct), true),
            (await signedIn.GetStringAsync("/", Ct), true),
            (await signedIn.GetStringAsync("/queue/mine", Ct), true),
        };

        foreach (var (html, framework) in pages)
        {
            html.ShouldContain("<html", Case.Sensitive);
            if (framework)
            {
                html.ShouldContain("blazor.web", Case.Sensitive, "the framework script must be there, or this check scans nothing");
            }

            InlineScriptTag().Matches(html).Select(m => m.Value).ShouldBeEmpty();
            StyleElement().IsMatch(html).ShouldBeFalse();
            EventHandlerAttribute().IsMatch(html).ShouldBeFalse();
        }

        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task The_Portal_renders_no_inline_script_a_style_element_or_an_event_handler_attribute()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("blazor.web", Case.Sensitive);
        InlineScriptTag().Matches(html).Select(m => m.Value).ShouldBeEmpty();
        StyleElement().IsMatch(html).ShouldBeFalse();
        EventHandlerAttribute().IsMatch(html).ShouldBeFalse();
    }

    [Theory]
    [InlineData("<script data-src=\"x.js\">alert(1)</script>", 1)]
    [InlineData("<script nosrc=\"x\"></script>", 1)]
    [InlineData("<script>alert(1)</script>", 1)]
    [InlineData("<script src=\"/a.js\"></script>", 0)]
    [InlineData("<script type=\"module\" src=\"/a.js\"></script>", 0)]
    [InlineData("<script defer\nsrc=\"/a.js\"></script>", 0)]
    public void The_inline_script_scan_sees_a_script_whose_attribute_only_ends_in_src(string html, int expected)
    {
        InlineScriptTag().Matches(html).Count.ShouldBe(expected);
    }

    // Without the import map component the modules load from their own paths, so each one must still be served (and not need a fingerprint to be found). The client does not
    // follow redirects: an anonymous request for a missing file is answered with a redirect to /signin, which a following client would report as 200.
    [Theory]
    [InlineData("/js/dialog.js")]
    [InlineData("/js/shortcuts.js")]
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/_content/SyntaxCircus.Blazor.Components/Components/Feedback/ReconnectModal.razor.js")]
    [InlineData("/_content/SyntaxCircus.Blazor.Components/fileDownload.js")]
    public async Task Every_module_the_Admin_imports_is_served_from_its_plain_path(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
    }

    [Fact]
    public async Task A_static_file_carries_the_decided_policy()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/js/dialog.js", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertBrowserPolicy(Policy(response), ["'self'", new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority)], loopbackImages: true);
    }

    [Fact]
    public async Task The_circuit_negotiate_endpoint_carries_the_decided_policy()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false }).SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertBrowserPolicy(Policy(response), ["'self'", new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority)], loopbackImages: true);
    }
}
