using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Hosting.Wiring;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// P09-T04 / D-045: <c>UseTechStrapWebHost</c> accepts per-path header rules that run after the shared security headers. The shared middleware sets <c>Referrer-Policy</c> and the
/// Content-Security-Policy when the response starts, so a value an endpoint sets itself would be overwritten; a rule runs after it and wins. These tests build a small host (TestServer) with the
/// real wiring, so they pin the mechanism for any host; the Portal's own rules are pinned in <c>TechStrap.Portal.Tests</c>.
/// </summary>
public sealed class PathHeaderRuleHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static bool UnderT(PathString path) => path.StartsWithSegments("/t");

    private static bool Attachment(PathString path) => path.StartsWithSegments("/t", out var rest) && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, "attachments", _];

    private static IReadOnlyList<PathHeaderRule> Rules() =>
    [
        PathHeaderRule.Set(UnderT, ("Referrer-Policy", "no-referrer"), ("Cache-Control", "no-store"), ("X-Robots-Tag", "noindex")),
        PathHeaderRule.Sandbox(Attachment),
    ];

    private static async Task<WebApplication> StartAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddTechStrapWebHost(builder.Configuration, "default-src 'self'; frame-ancestors 'none'");
        var app = builder.Build();
        configure(app);
        await app.StartAsync(Ct);
        return app;
    }

    private static void Pipeline(WebApplication app, IReadOnlyList<PathHeaderRule> rules, params string[] downloadPrefixes)
    {
        app.UseTechStrapWebHost(rules, downloadPrefixes);
        app.UseTechStrapErrorPages();
        app.MapGet("/t/page", (HttpContext context) =>
        {
            // What a page might set for itself: the shared middleware would overwrite the policy, and the rule must overwrite both.
            context.Response.Headers.CacheControl = "max-age=60";
            context.Response.Headers["Referrer-Policy"] = "unsafe-url";
            return "page";
        });
        app.MapGet("/t/{token}/attachments/{id}", (string id) => id == "missing" ? Results.NotFound() : Results.Text("file"));
        app.MapGet("/other", () => "other");
        app.MapGet("/not-found", () => "the not-found page");
    }

    private static string[] Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    private static string[] Policy(HttpResponseMessage response) => Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    [Fact]
    public async Task A_rule_overrides_what_the_shared_headers_set_and_what_the_endpoint_set()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/page", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], "the shared middleware's value and the endpoint's own value must both lose");
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
        Header(response, "X-Robots-Tag").ShouldBe(["noindex"]);
        Policy(response).ShouldNotContain("sandbox", "only the attachment route is sandboxed, so a page keeps the normal policy");
        Policy(response).ShouldContain("default-src 'self'");
    }

    [Fact]
    public async Task A_path_no_rule_matches_keeps_the_shared_headers_exactly()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/other", Ct);

        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        Header(response, "Cache-Control").ShouldBeEmpty();
        Header(response, "X-Robots-Tag").ShouldBeEmpty();
        Policy(response).ShouldNotContain("sandbox");
    }

    [Theory]
    [InlineData("/t/page")]
    [InlineData("/T/PAGE")]
    [InlineData("/t/page/")]
    public async Task A_rule_matches_without_regard_to_case_or_a_trailing_slash(string path)
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], path);
        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
    }

    [Fact]
    public async Task The_sandbox_rule_appends_a_bare_sandbox_directive_to_a_delivered_file_only()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var page = await client.GetAsync("/t/page", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(file).ShouldContain("default-src 'self'", "the page policy is still there, with sandbox on top");
        Header(file, "Referrer-Policy").ShouldBe(["no-referrer"], "the file is under /t, so the /t rule applies to it as well");
        Policy(page).ShouldNotContain("sandbox");
    }

    [Fact]
    public async Task The_sandbox_rule_does_not_sandbox_an_error_answer_but_the_other_rules_still_apply_to_it()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/abc/attachments/missing", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Policy(response).ShouldNotContain("sandbox");
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
    }

    // The 404 for an unknown /t path is re-executed at /not-found, where the request path is no longer /t/...: the rule must be decided from the path the visitor asked for.
    [Fact]
    public async Task The_rules_apply_to_the_re_executed_404_page_of_a_matching_path_and_not_to_other_404s()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var ticket = await client.GetAsync("/t/no-such-thing", Ct);
        using var elsewhere = await client.GetAsync("/no-such-thing", Ct);

        ticket.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ticket.Content.ReadAsStringAsync(Ct)).ShouldBe("the not-found page");
        Header(ticket, "Referrer-Policy").ShouldBe(["no-referrer"]);
        Header(ticket, "Cache-Control").ShouldBe(["no-store"]);
        Header(ticket, "X-Robots-Tag").ShouldBe(["noindex"]);
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Header(elsewhere, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        Header(elsewhere, "Cache-Control").ShouldBeEmpty();
    }

    [Fact]
    public async Task Several_matching_rules_apply_in_the_order_given_and_a_later_one_wins()
    {
        IReadOnlyList<PathHeaderRule> rules =
        [
            PathHeaderRule.Set(UnderT, ("X-Probe", "first"), ("Cache-Control", "no-store")),
            PathHeaderRule.Set(UnderT, ("X-Probe", "second")),
        ];
        await using var app = await StartAsync(a => Pipeline(a, rules));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/page", Ct);

        Header(response, "X-Probe").ShouldBe(["second"]);
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
    }

    [Fact]
    public async Task The_download_prefix_argument_still_works_as_a_sandbox_rule_without_any_other_rule()
    {
        await using var app = await StartAsync(a => Pipeline(a, [], "/t"));
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var other = await client.GetAsync("/other", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(other).ShouldNotContain("sandbox");
        Header(file, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], "no header rule was given");
    }

    [Fact]
    public async Task The_existing_signature_without_rules_is_unchanged()
    {
        await using var app = await StartAsync(a =>
        {
            a.UseTechStrapWebHost("/t");
            a.MapGet("/t/{token}/attachments/{id}", () => "file");
            a.MapGet("/other", () => "other");
        });
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var other = await client.GetAsync("/other", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(other).ShouldNotContain("sandbox");
    }

    private static bool UnderKb(PathString path) => path.StartsWithSegments("/kb");

    private static void SuccessPipeline(WebApplication app, params PathHeaderRule[] rules)
    {
        app.UseTechStrapWebHost(rules);
        app.UseTechStrapErrorPages();
        app.MapGet("/kb/page", (HttpContext context) =>
        {
            // What a page might set for itself: the rule must win over it on a delivered page.
            context.Response.Headers.CacheControl = "private";
            return "page";
        });
        app.MapGet("/kb/gone", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        });
        app.MapGet("/kb/busy", () => Results.StatusCode(StatusCodes.Status429TooManyRequests));
        app.MapGet("/kb/down", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        app.MapGet("/kb/moved", () => Results.Redirect("/kb/page"));
        app.MapGet("/kb/created", () => Results.StatusCode(StatusCodes.Status201Created));
        app.MapGet("/other", () => "other");
        app.MapGet("/not-found", () => "the not-found page");
    }

    [Theory]
    [InlineData("/kb/page", HttpStatusCode.OK, true)]
    [InlineData("/kb/created", HttpStatusCode.Created, true)]
    [InlineData("/kb/gone", HttpStatusCode.NotFound, false)]
    [InlineData("/kb/busy", HttpStatusCode.TooManyRequests, false)]
    [InlineData("/kb/down", HttpStatusCode.ServiceUnavailable, false)]
    [InlineData("/kb/moved", HttpStatusCode.Redirect, false)]
    [InlineData("/other", HttpStatusCode.OK, false)]
    public async Task A_success_only_rule_sets_its_header_on_a_2xx_answer_for_a_matching_path_and_on_nothing_else(string path, HttpStatusCode status, bool applied)
    {
        await using var app = await StartAsync(a => SuccessPipeline(a, PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"), ("X-Probe", "kb"))));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status);
        Header(response, "X-Probe").ShouldBe(applied ? ["kb"] : []);
        if (applied)
        {
            Header(response, "Cache-Control").ShouldBe(["public, max-age=60"], "the rule wins over the value the endpoint set itself");
        }
        else
        {
            Header(response, "Cache-Control").ShouldNotContain("public, max-age=60");
        }
    }

    [Fact]
    public async Task A_success_only_rule_leaves_what_an_error_answer_set_for_itself_and_the_page_that_replaces_a_404_carries_no_public_header()
    {
        await using var app = await StartAsync(a => SuccessPipeline(a, PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"))));
        using var client = app.GetTestClient();

        using var gone = await client.GetAsync("/kb/gone", Ct);

        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync(Ct)).ShouldBe("the not-found page");
        Header(gone, "Cache-Control").ShouldBe(["no-store"]);
    }

    [Fact]
    public async Task A_success_only_rule_and_an_every_status_rule_apply_in_the_order_given()
    {
        await using var app = await StartAsync(a => SuccessPipeline(
            a,
            PathHeaderRule.Set(UnderKb, ("X-Robots-Tag", "noindex"), ("Cache-Control", "no-store")),
            PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"))));
        using var client = app.GetTestClient();

        using var page = await client.GetAsync("/kb/page", Ct);
        using var gone = await client.GetAsync("/kb/gone", Ct);

        Header(page, "Cache-Control").ShouldBe(["public, max-age=60"]);
        Header(page, "X-Robots-Tag").ShouldBe(["noindex"]);
        Header(gone, "Cache-Control").ShouldBe(["no-store"]);
        Header(gone, "X-Robots-Tag").ShouldBe(["noindex"]);
    }

    // The Admin calls UseTechStrapWebHost with a download prefix and no rule. Adding SetOnSuccess must change nothing for it: no header on an ordinary page or an error, and a sandbox on a delivered download only.
    [Fact]
    public async Task The_admins_signature_with_only_a_download_prefix_sets_no_cache_header_and_sandboxes_a_delivered_download_only()
    {
        await using var app = await StartAsync(a =>
        {
            a.UseTechStrapWebHost("/attachments");
            a.UseTechStrapErrorPages();
            a.MapGet("/attachments/{id}", (string id) => id == "missing" ? Results.NotFound() : Results.Text("file"));
            a.MapGet("/queue", () => "page");
            a.MapGet("/not-found", () => "the not-found page");
        });
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/attachments/1", Ct);
        using var missing = await client.GetAsync("/attachments/missing", Ct);
        using var page = await client.GetAsync("/queue", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(missing).ShouldNotContain("sandbox");
        Policy(page).ShouldNotContain("sandbox");
        foreach (var response in new[] { file, missing, page })
        {
            Header(response, "Cache-Control").ShouldBeEmpty();
            Header(response, "X-Robots-Tag").ShouldBeEmpty();
            Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        }
    }

    [Fact]
    public void A_success_only_rule_needs_a_predicate_and_at_least_one_named_header()
    {
        Should.Throw<ArgumentNullException>(() => PathHeaderRule.SetOnSuccess(null!, ("A", "b")));
        Should.Throw<ArgumentException>(() => PathHeaderRule.SetOnSuccess(UnderKb));
        Should.Throw<ArgumentException>(() => PathHeaderRule.SetOnSuccess(UnderKb, (" ", "b")));
    }

    [Fact]
    public void A_rule_needs_a_predicate_and_at_least_one_header()
    {
        Should.Throw<ArgumentNullException>(() => PathHeaderRule.Set(null!, ("A", "b")));
        Should.Throw<ArgumentNullException>(() => PathHeaderRule.Sandbox(null!));
        Should.Throw<ArgumentException>(() => PathHeaderRule.Set(UnderT));
        Should.Throw<ArgumentException>(() => PathHeaderRule.Set(UnderT, (" ", "b")));
    }
}
