using System.Net;
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
    [InlineData("/")]
    [InlineData("/no-such-page")]
    public async Task The_Portal_pages_carry_the_headers(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        AssertCommonHeaders(response, "Portal " + path);
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
