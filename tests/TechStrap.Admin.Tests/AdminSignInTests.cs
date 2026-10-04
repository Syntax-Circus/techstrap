using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed class AdminSignInTests
{
    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task An_anonymous_request_for_a_page_is_sent_to_the_landing_page_with_a_local_return_url()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.ShouldBe("/signin?returnUrl=%2F");
    }

    [Fact]
    public async Task The_landing_page_is_anonymous_has_one_sign_in_form_and_opens_no_circuit()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/signin?returnUrl=%2Ftickets%2FORB-1", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.QuerySelector("article.ts-window")!.TextContent.ShouldContain("Sign in");
        var form = page.QuerySelectorAll("form[action='/signin/start'][method=get]").ShouldHaveSingleItem();
        form.QuerySelector("input[name=returnUrl]")!.GetAttribute("value").ShouldBe("/tickets/ORB-1");
        page.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href")!.ShouldContain("css/app.css");
        html.ShouldNotContain("blazor.web.js");
    }

    [Fact]
    public async Task The_landing_page_never_echoes_a_hostile_return_url()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/signin?returnUrl=https%3A%2F%2Fevil.example%2F", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        html.ShouldNotContain("evil.example");
    }

    [Fact]
    public async Task Static_assets_health_and_the_error_pages_are_anonymous()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        foreach (var path in new[] { "/health/live", "/health/ready", "/brand/mark.svg", "/favicon.ico", "/not-found", "/error", "/_styleguide" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Starting_sign_in_redirects_to_the_provider_with_the_code_flow_pkce_and_offline_access()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/signin/start?returnUrl=%2Ftickets%2FORB-1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe("https://idp.test/application/o/authorize/");
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe("techstrap-admin-test");
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"].ShouldEndWith("/signin-oidc");
        query["scope"]!.Split(' ').ShouldBe(["openid", "profile", "email", "offline_access"], ignoreOrder: true);
    }

    [Fact]
    public async Task Signing_out_needs_an_antiforgery_token()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.PostAsync("/signout", new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_open_a_circuit()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBeOneOf(302, 401);
    }

    [Fact]
    public async Task Signing_out_with_the_form_token_redirects_to_the_provider_end_session_endpoint()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);

        var shell = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(await shell.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var form = page.QuerySelector("form[action='/signout'][method=post]").ShouldNotBeNull();
        var token = form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        using var post = new HttpRequestMessage(HttpMethod.Post, "/signout") { Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]) };
        foreach (var cookie in shell.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]))
        {
            post.Headers.Add("Cookie", cookie);
        }

        using var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe("https://idp.test/application/o/techstrap-admin/end-session/");
    }
}
