using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the article editor (PHASE-08 T17): every agent may open it, every call carries the agent's token, and the "View on portal" link is built from the
/// portal address in the Admin's own configuration. Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class KbEditorHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AdminFactory FactoryWithArticle(string status, string? portalUrl = "https://help.example.com")
    {
        var settings = new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = portalUrl };
        var factory = new AdminFactory(settings: settings);
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, OrbitlyId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", new KbArticleDto(
                ArticleId, OrbitlyId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps", status, Guid.NewGuid(), Now.AddDays(-2), Now.AddHours(-1),
                status == KbArticleStatuses.Published ? Now.AddDays(-1) : null, 3));
        return factory;
    }

    [Theory]
    [InlineData("/kb/new")]
    [InlineData("/kb/dddddddd-0000-0000-0000-000000000001")]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_the_article_is_never_read()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync($"/kb/{ArticleId}", Ct);

        html.ShouldNotContain("Reset your password");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Every_agent_opens_a_new_article_and_the_page_reads_only_the_lookups(string who)
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        var principal = who == "Admin" ? AdminTestPrincipal.Admin : AdminTestPrincipal.Agent;
        using var client = factory.CreateClient().SignedInAs(principal);

        var html = await client.GetStringAsync("/kb/new", Ct);
        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);

        page.QuerySelector("h1")!.TextContent.ShouldBe("New article");
        page.QuerySelector("#ts-kb-body")!.TagName.ShouldBe("TEXTAREA");
        page.QuerySelectorAll("#ts-kb-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Orbitly"]);
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/kb/categories", "/api/products"]);
        factory.Api.AssertEveryCallBore(principal);
    }

    [Fact]
    public async Task A_published_article_shows_its_status_and_a_portal_link_built_from_the_admins_own_setting()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Published);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync($"/kb/{ArticleId}", Ct), Ct);

        page.QuerySelector(".ts-kb-status .ts-pill")!.TextContent.ShouldBe("Published");
        var link = page.QuerySelector("a.ts-kb-portal-link")!;
        link.GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        page.QuerySelector("#ts-kb-title")!.GetAttribute("value").ShouldBe("Reset your password");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Theory]
    [InlineData(KbArticleStatuses.Draft, "https://help.example.com")]
    [InlineData(KbArticleStatuses.Published, "")]
    public async Task A_draft_and_a_missing_portal_setting_both_leave_the_link_out(string status, string portalUrl)
    {
        await using var factory = FactoryWithArticle(status, portalUrl);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync($"/kb/{ArticleId}", Ct), Ct);

        page.QuerySelectorAll("a.ts-kb-portal-link").ShouldBeEmpty();
        page.QuerySelector("#ts-kb-title")!.GetAttribute("value").ShouldBe("Reset your password");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_article_that_is_missing_says_so_and_draws_no_form()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        factory.Api.OnProblem(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", HttpStatusCode.NotFound, "kb-article-not-found", "No such article.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync($"/kb/{ArticleId}", Ct);

        html.ShouldContain("This article no longer exists.");
        html.ShouldNotContain("ts-kb-form");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_segment_that_is_not_an_article_id_is_not_an_article()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync("/kb/not-an-id", Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("ts-kb-form");
    }
}
