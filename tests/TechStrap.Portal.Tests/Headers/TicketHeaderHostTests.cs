using System.Net;
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>
/// Review Focus 2 at the host: whatever the Portal answers under <c>/t</c> carries <c>Referrer-Policy: no-referrer</c>, <c>Cache-Control: no-store</c> and <c>X-Robots-Tag: noindex</c>, and
/// only the attachment route is sandboxed, so the ticket page keeps the normal policy. The shared security-header middleware overwrites a value a page sets itself; these assert the final
/// response. There is no ticket page until PHASE-09b, so the 404 for an unknown <c>/t</c> path is checked (its headers must apply anyway), and a probe answers 200 on paths no later route can match.
/// </summary>
public sealed class TicketHeaderHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    private static readonly string AttachmentPath = $"/t/{Token}/attachments/{Guid.NewGuid()}";

    // A page under /t that no Portal route will ever match (the ticket page is /t/{token}), and the real shape of the attachment route with an id the pass-through (a Guid route) cannot match.
    private static readonly Action<Microsoft.Extensions.DependencyInjection.IServiceCollection> Probes = OkProbeStartupFilter.Add(path =>
        path.StartsWithSegments("/t/probe-token/probe-page") || path.StartsWithSegments("/t/probe-token/attachments/probe-id"));

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    private static string[] Policy(HttpResponseMessage response) => Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    private static void AssertTicketHeaders(HttpResponseMessage response, string where)
    {
        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], where);
        Header(response, "Cache-Control").ShouldBe(["no-store"], where);
        Header(response, "X-Robots-Tag").ShouldBe(["noindex"], where);
        Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"], where);
        Header(response, "X-Frame-Options").ShouldBe(["DENY"], where);
    }

    [Theory]
    [InlineData("/t/x")]
    [InlineData("/T/X")]
    [InlineData("/t")]
    [InlineData("/t/")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567")]
    public async Task An_unknown_ticket_path_is_the_neutral_404_with_the_ticket_headers(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        html.ShouldContain("Page not found");
        AssertTicketHeaders(response, path);
        Policy(response).ShouldNotContain("sandbox", "the not-found page is an ordinary page");
        Policy(response).ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task A_delivered_page_under_t_has_the_ticket_headers_and_keeps_the_normal_policy()
    {
        await using var factory = new PortalFactory(configureServices: Probes);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/t/probe-token/probe-page", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("probe", "the probe must really have answered, or this proves nothing");
        AssertTicketHeaders(response, "page");
        Policy(response).ShouldNotContain("sandbox");
        Policy(response).ShouldContain("script-src 'self'");
        Policy(response).ShouldContain("form-action 'self'");
    }

    [Fact]
    public async Task A_delivered_attachment_has_the_ticket_headers_and_a_sandbox_on_top_of_the_page_policy()
    {
        await using var factory = new PortalFactory(configureServices: Probes);
        using var client = factory.CreateClient();

        // The probe answers the attachment shape; the id is not a guid, which a later pass-through route will not match, so the response is the probe's 200.
        using var response = await client.GetAsync("/t/probe-token/attachments/probe-id", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("probe");
        AssertTicketHeaders(response, "attachment");
        Policy(response).Count(directive => directive == "sandbox").ShouldBe(1);
        Policy(response).ShouldContain("script-src 'self'", "the page policy is still there, with sandbox on top");
    }

    [Fact]
    public async Task A_missing_attachment_is_the_404_page_with_the_ticket_headers_and_is_not_sandboxed()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(AttachmentPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertTicketHeaders(response, "missing attachment");
        Policy(response).ShouldNotContain("sandbox");
    }

    [Theory]
    [InlineData("/p/paperplane/kb/search", HttpStatusCode.NotFound)]
    [InlineData("/p/paperplane/kb/guides/dark-mode", HttpStatusCode.NotFound)]
    [InlineData("/no-such-page", HttpStatusCode.NotFound)]
    [InlineData("/not-found", HttpStatusCode.OK)]
    [InlineData("/error", HttpStatusCode.OK)]
    [InlineData("/tx", HttpStatusCode.NotFound)]
    [InlineData("/ticket/x", HttpStatusCode.NotFound)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    public async Task Everything_outside_t_keeps_the_shared_headers_and_is_never_sandboxed_or_forced_no_store(string path, HttpStatusCode status)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status, path);
        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], path);
        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
        Header(response, "Cache-Control").ShouldNotContain("no-store", path);
        Policy(response).ShouldNotContain("sandbox", path);
    }

    [Fact]
    public async Task A_500_under_t_in_production_keeps_the_ticket_headers_and_is_not_sandboxed()
    {
        await using var factory = new PortalFactory("Production", configureServices: ThrowProbeStartupFilter.Add(path => path.StartsWithSegments("/t/probe-token/boom")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/t/probe-token/boom", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Something went wrong.", Case.Insensitive, "the real error page answered");
        AssertTicketHeaders(response, "500");
        Policy(response).ShouldNotContain("sandbox", "the error page is an ordinary page");
    }

    [Fact]
    public async Task The_header_rules_do_not_depend_on_the_environment()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            await using var factory = new PortalFactory(environment, configureServices: Probes);
            using var client = factory.CreateClient();

            using var page = await client.GetAsync("/t/probe-token/probe-page", Ct);
            using var missing = await client.GetAsync("/t/x", Ct);

            AssertTicketHeaders(page, environment + " page");
            AssertTicketHeaders(missing, environment + " 404");
        }
    }
}
