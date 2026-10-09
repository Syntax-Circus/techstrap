using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed class AdminSignInTests
{
    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// SyntaxCircus.Blazor.Auth's refresh service reads the OIDC options under the framework default scheme name, so the Admin must register its scheme under that exact name
    /// or token refresh silently fails.
    /// </summary>
    [Fact]
    public void The_oidc_scheme_is_registered_under_the_name_Blazor_Auth_refreshes()
    {
        using var factory = new AdminFactory();

        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        options.Authority.ShouldNotBeNullOrWhiteSpace();
        options.ClientId.ShouldNotBeNullOrWhiteSpace();
    }

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
    public async Task A_plain_get_to_the_provider_remote_sign_out_address_does_not_sign_anyone_out()
    {
        await using var factory = new AdminFactory();

        foreach (var principal in new AdminTestPrincipal?[] { null, AdminTestPrincipal.Agent })
        {
            using var client = NoRedirectClient(factory);
            if (principal is not null)
            {
                client.SignedInAs(principal);
            }

            using var response = await client.GetAsync("/signout-oidc", TestContext.Current.CancellationToken);

            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
            cookies.ShouldNotContain(c => c.Contains("techstrap.admin"));
            response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", CookieSecurePolicy.Always)]
    public async Task The_session_cookie_is_secure_outside_development(string environment, CookieSecurePolicy expected)
    {
        await using var factory = new AdminFactory(environment);
        _ = factory.Server;

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");

        options.Cookie.SecurePolicy.ShouldBe(expected);
        options.Cookie.HttpOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task Anonymous_not_found_and_error_pages_are_static_and_start_no_circuit_while_a_page_for_an_agent_does()
    {
        await using var factory = new AdminFactory();
        using var anonymous = NoRedirectClient(factory);
        using var agent = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);

        foreach (var path in new[] { "/not-found", "/error" })
        {
            (await anonymous.GetStringAsync(path, TestContext.Current.CancellationToken)).ShouldNotContain("\"type\":\"server\"", customMessage: path);
        }

        (await agent.GetStringAsync("/", TestContext.Current.CancellationToken)).ShouldContain("\"type\":\"server\"");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task Signing_out_removes_the_agent_tokens_from_the_server_cache()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);
        var cache = factory.Services.GetRequiredService<IServerTokenCache>();
        var key = factory.Services.GetRequiredService<IUserTokenCacheKeyProvider>().GetCacheKey(AdminTestPrincipal.Agent.Subject)!;
        (await cache.GetAsync(key, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var shell = await client.GetAsync("/", TestContext.Current.CancellationToken);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        var page = await new HtmlParser().ParseDocumentAsync(await shell.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var token = page.QuerySelector("form[action='/signout'] input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        using var post = new HttpRequestMessage(HttpMethod.Post, "/signout") { Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]) };
        foreach (var cookie in shell.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]))
        {
            post.Headers.Add("Cookie", cookie);
        }

        using var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await cache.GetAsync(key, TestContext.Current.CancellationToken)).ShouldBeNull();
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
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
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
