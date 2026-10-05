using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the knowledge base list (PHASE-08). Every agent may open it, and every call it makes carries the agent's token.
/// Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class KbListHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AdminFactory FactoryWithKbData()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, OrbitlyId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, "/api/kb/articles", new PagedResponse<KbArticleListItemDto>(
                [new KbArticleListItemDto(Guid.NewGuid(), OrbitlyId, CategoryId, "reset-password", "Reset your password", KbArticleStatuses.Published, Now.AddHours(-3))], 1, 25, 1));
        return factory;
    }

    private static HttpClient NoRedirectClient(AdminFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithKbData();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_only_the_me_call_is_made()
    {
        await using var factory = FactoryWithKbData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync("/kb", Ct);

        html.ShouldNotContain("Reset your password");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Every_agent_sees_the_article_list_with_its_data_and_the_rail_link(string who)
    {
        await using var factory = FactoryWithKbData();
        var principal = who == "Admin" ? AdminTestPrincipal.Admin : AdminTestPrincipal.Agent;
        using var client = factory.CreateClient().SignedInAs(principal);

        var html = await client.GetStringAsync("/kb", Ct);
        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);

        page.QuerySelector("h1")!.TextContent.ShouldBe("Knowledge base");
        page.QuerySelector("tbody tr a")!.TextContent.ShouldBe("Reset your password");
        page.QuerySelector("tbody tr")!.Children[1].TextContent.ShouldBe("Orbitly");
        page.QuerySelector("tbody tr")!.Children[2].TextContent.ShouldBe("Account");
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.TextContent.Trim().ShouldBe("Knowledge base");
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.GetAttribute("aria-current").ShouldBe("page");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/articles" && r.Query == "?page=1&pageSize=25");
        factory.Api.AssertEveryCallBore(principal);
    }

    [Fact]
    public async Task The_filters_in_the_address_reach_the_api_as_the_query_string()
    {
        await using var factory = FactoryWithKbData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        await client.GetStringAsync($"/kb?product={OrbitlyId}&status=Draft&search=reset", Ct);

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/articles" && r.Query == $"?productId={OrbitlyId}&includeShared=false&status=Draft&text=reset&page=1&pageSize=25");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }
}
