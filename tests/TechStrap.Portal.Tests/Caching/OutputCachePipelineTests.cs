using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Hosting.Wiring;
using TechStrap.Portal.Caching;
using TechStrap.Portal.Headers;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// PHASE-09c Review Focus 3 (cache safety), the mechanism: a small host (TestServer) with the real wiring in the order <c>Program.cs</c> uses (the shared headers and the Portal's header rules, the error pages, then the output
/// cache) and a counting endpoint at each path. It proves what the cache keeps, what it never keeps and that a hit still carries the request's own headers, without depending on a page; the real pages are pinned in
/// <c>KbPageCacheHostTests</c>. The spike (see the plan) found that a stored copy replays the first request's <c>X-Correlation-Id</c>; <see cref="PortalOutputCache.UsePortalOutputCache"/> sets the right one again.
/// </summary>
public sealed class OutputCachePipelineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class Counter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public int Next() => Interlocked.Increment(ref _calls);
    }

    private static async Task<WebApplication> StartAsync(Counter counter)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddTechStrapWebHost(builder.Configuration, "default-src 'self'; frame-ancestors 'none'");
        builder.Services.AddPortalOutputCache();
        var app = builder.Build();
        app.UseTechStrapWebHost(PortalHeaderRules.Rules(new TechStrap.Portal.Settings.PortalOptions()));
        app.UseTechStrapErrorPages();
        app.UsePortalOutputCache();
        app.Map("/{**path}", (HttpContext context) =>
        {
            var call = counter.Next();
            var path = context.Request.Path.Value!;
            context.Response.Headers["X-Probe-Call"] = call.ToString();
            if (path.EndsWith("/gone", StringComparison.Ordinal))
            {
                return Results.NotFound($"gone {call}");
            }

            if (path.EndsWith("/busy", StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (path.EndsWith("/down", StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (path.EndsWith("/cookie", StringComparison.Ordinal))
            {
                context.Response.Cookies.Append("probe", "1");
            }

            return Results.Text($"page {call}", "text/html");
        });
        await app.StartAsync(Ct);
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? host = null, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request, Ct);
    }

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/reset-password")]
    public async Task A_kb_page_asked_twice_is_made_once_and_the_second_answer_is_the_stored_one(string path)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        counter.Calls.ShouldBe(1);
        (await first.Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        Header(first, "Age").ShouldBeEmpty();
        Header(second, "Age").ShouldNotBeEmpty("the second answer came from the cache");
    }

    [Fact]
    public async Task A_cached_answer_carries_the_security_headers_the_browser_rule_and_the_requests_own_correlation_id()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb", correlationId: "cid-one");
        using var second = await GetAsync(client, "/p/paperplane/kb", correlationId: "cid-two");
        using var third = await GetAsync(client, "/p/paperplane/kb");

        counter.Calls.ShouldBe(1);
        Header(first, "X-Correlation-Id").ShouldBe(["cid-one"]);
        Header(second, "X-Correlation-Id").ShouldBe(["cid-two"], "a stored copy replays the first request's id unless it is set again");
        Header(third, "X-Correlation-Id").ShouldHaveSingleItem().ShouldNotBe("cid-one");
        foreach (var response in new[] { first, second, third })
        {
            Header(response, "Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("default-src 'self'");
            Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
            Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
            Header(response, "X-Frame-Options").ShouldBe(["DENY"]);
            response.Headers.CacheControl!.ToString().ShouldBe(PortalCachePaths.BrowserCacheControl);
            Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("/p/paperplane/kb/gone", HttpStatusCode.NotFound)]
    [InlineData("/p/paperplane/kb/busy", HttpStatusCode.TooManyRequests)]
    [InlineData("/p/paperplane/kb/down", HttpStatusCode.ServiceUnavailable)]
    public async Task A_404_a_429_and_a_503_are_never_stored_and_never_get_a_public_header(string path, HttpStatusCode status)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        first.StatusCode.ShouldBe(status);
        second.StatusCode.ShouldBe(status);
        counter.Calls.ShouldBe(2, "an error answer is made again every time");
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
        second.Headers.CacheControl?.Public.ShouldNotBe(true);
        Header(second, "Age").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_that_sets_a_cookie_is_never_stored()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb/cookie");
        using var second = await GetAsync(client, "/p/paperplane/kb/cookie");

        counter.Calls.ShouldBe(2);
        Header(first, "Set-Cookie").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("/p/paperplane/kb/search?q=router")]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/p/paperplane/suggest?q=a")]
    [InlineData("/p/paperplane")]
    [InlineData("/t/abc")]
    [InlineData("/t/abc/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/not-found")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    public async Task The_search_page_the_forms_the_suggest_adapter_tickets_and_every_other_page_are_never_kept(string path)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        counter.Calls.ShouldBe(2, path);
        Header(second, "Age").ShouldBeEmpty();
        first.Headers.CacheControl?.Public.ShouldNotBe(true, path);
    }

    [Fact]
    public async Task The_search_page_and_the_ticket_and_form_pages_tell_the_browser_not_to_keep_them_either()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        foreach (var path in new[] { "/p/paperplane/kb/search?q=x", "/t/abc", "/p/paperplane/contact", "/p/paperplane/suggest" })
        {
            using var response = await GetAsync(client, path);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue(path);
        }
    }

    [Fact]
    public async Task The_host_is_part_of_the_key_two_hosts_two_entries()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        (await (await GetAsync(client, "/p/paperplane/kb/accounts", host: "one.example")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts", host: "two.example")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 2");
        counter.Calls.ShouldBe(2, "each host has its own entry");

        (await (await GetAsync(client, "/p/paperplane/kb/accounts", host: "one.example")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts", host: "two.example")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 2");
        counter.Calls.ShouldBe(2, "each host is then served from its own stored copy");
    }

    [Fact]
    public async Task Only_the_page_value_changes_the_key_and_a_category_with_any_other_query_is_answered_but_never_stored()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        (await (await GetAsync(client, "/p/paperplane/kb/accounts")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");

        // Any other raw query is answered afresh each time (the page's links repeat the address bar's own spelling, so a stored copy must never serve another spelling).
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?utm=1")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 2");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?utm=1")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 3");
        counter.Calls.ShouldBe(3, "a category with another query value is not kept");

        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=2")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 4");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=2")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 4");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=3")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 5");
        counter.Calls.ShouldBe(5, "each plain page number is its own key");

        foreach (var spelling in new[] { "?PAGE=2", "?Page=2&x=1" })
        {
            await GetAsync(client, "/p/paperplane/kb/accounts" + spelling);
            await GetAsync(client, "/p/paperplane/kb/accounts" + spelling);
        }

        counter.Calls.ShouldBe(9, "no other spelling of a page number is stored: every request reached the page");
    }

    [Fact]
    public async Task A_cache_key_is_never_shared_between_products_categories_or_articles()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        var bodies = new List<string>();
        foreach (var path in new[] { "/p/acme/kb", "/p/orbitly/kb", "/p/acme/kb/accounts", "/p/acme/kb/billing", "/p/acme/kb/accounts/reset", "/p/orbitly/kb/accounts/reset" })
        {
            bodies.Add(await (await GetAsync(client, path)).Content.ReadAsStringAsync(Ct));
        }

        counter.Calls.ShouldBe(6);
        bodies.Distinct().Count().ShouldBe(6, "six different addresses, six different stored pages");
    }

    [Theory]
    [InlineData("?page=abc")]
    [InlineData("?page=0")]
    [InlineData("?page=99999999999")]
    [InlineData("?page=1&page=2")]
    public async Task A_page_value_that_is_not_a_plain_page_number_is_answered_but_never_stored(string query)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb/accounts" + query);
        using var second = await GetAsync(client, "/p/paperplane/kb/accounts" + query);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        counter.Calls.ShouldBe(2, query);
    }

    [Fact]
    public async Task A_post_is_never_stored()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await client.PostAsync("/p/paperplane/kb/accounts", new StringContent("x"), Ct);
        using var second = await client.PostAsync("/p/paperplane/kb/accounts", new StringContent("x"), Ct);

        counter.Calls.ShouldBe(2);
    }
}
