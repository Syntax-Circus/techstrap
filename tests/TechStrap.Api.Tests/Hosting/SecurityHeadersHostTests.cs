using System.Net;
using TechStrap.Hosting.Wiring;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The headers every response of the three web hosts carries (PHASE-07c): no sniffing, no framing, a strict referrer, no camera, microphone or location. The
/// Content-Security-Policy itself is pinned in <c>ContentSecurityPolicyHostTests</c>. Each page kind is checked, because the static pages and the re-executed error pages
/// are separate paths through the pipeline.
/// </summary>
public sealed class SecurityHeadersHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void AssertCommonHeaders(HttpResponseMessage response, string where)
    {
        string Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

        Header("X-Content-Type-Options").ShouldBe("nosniff", where);
        Header("X-Frame-Options").ShouldBe("DENY", where);
        Header("Referrer-Policy").ShouldBe("strict-origin-when-cross-origin", where);
        Header("Permissions-Policy").ShouldContain("camera=()", Case.Sensitive, where);
        Header("Permissions-Policy").ShouldContain("microphone=()", Case.Sensitive, where);
        Header("Permissions-Policy").ShouldContain("geolocation=()", Case.Sensitive, where);
        Header("Content-Security-Policy").ShouldContain("frame-ancestors 'none'", Case.Sensitive, where);
    }

    [Theory]
    [InlineData("/signin")]
    [InlineData("/error")]
    public async Task The_Admin_static_pages_carry_the_headers(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertCommonHeaders(response, "Admin " + path);
    }

    [Fact]
    public async Task The_Admin_signed_in_page_and_the_re_executed_404_carry_the_headers()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var home = await client.GetAsync("/", Ct);
        using var missing = await client.GetAsync("/no-such-page", Ct);

        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertCommonHeaders(home, "Admin /");
        AssertCommonHeaders(missing, "Admin 404");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task The_Admin_attachment_download_is_sandboxed_on_top_of_the_page_policy()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldContain("sandbox", "a download must never run script in the Admin's origin");
        policy.ShouldContain("frame-ancestors 'none'", "the page policy is still there");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/no-such-page", HttpStatusCode.NotFound)]
    public async Task The_Portal_pages_carry_the_headers(string path, HttpStatusCode status)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status);
        AssertCommonHeaders(response, "Portal " + path);
    }

    [Theory]
    [InlineData("/attachments/{0}", HttpStatusCode.OK, true)]
    [InlineData("/ATTACHMENTS/{0}", HttpStatusCode.OK, true)]
    [InlineData("/attachments-x", HttpStatusCode.NotFound, false)]
    public async Task The_download_sandbox_follows_the_attachments_path_segment_exactly(string pathFormat, HttpStatusCode status, bool sandboxed)
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync(string.Format(pathFormat, id), Ct);

        response.StatusCode.ShouldBe(status);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.Contains("sandbox").ShouldBe(sandboxed);
        policy.ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task The_sandbox_is_kept_when_the_API_has_no_such_attachment_and_the_404_page_is_re_executed()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldContain("sandbox");
        policy.ShouldContain("frame-ancestors 'none'");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Theory]
    [InlineData("", "sandbox")]
    [InlineData("frame-ancestors 'none'", "frame-ancestors 'none'; sandbox")]
    [InlineData("frame-ancestors 'none'; sandbox", "frame-ancestors 'none'; sandbox")]
    [InlineData("frame-ancestors 'none'; SANDBOX", "frame-ancestors 'none'; SANDBOX")]
    [InlineData("script-src 'self' sandbox.example.com", "script-src 'self' sandbox.example.com; sandbox")]
    [InlineData("frame-ancestors 'none'; sandbox allow-scripts", "frame-ancestors 'none'; sandbox")]
    public void The_sandbox_directive_is_matched_as_a_whole_directive_and_a_weaker_one_is_replaced(string existing, string expected)
    {
        BrowserHostExtensions.WithSandbox(existing).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    public async Task The_Api_responses_carry_the_headers(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertCommonHeaders(response, "Api " + path);
    }
}
