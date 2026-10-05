using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the categories page (PHASE-08 T19). Every agent may open it and edit; only an admin is offered Delete (the page is not behind <c>AdminOnly</c>, so the
/// button's absence for an agent is the thing to pin). Every call carries the agent's token.
/// </summary>
public sealed class KbCategoriesHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static AdminFactory FactoryWithCategories()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)
            [
                new KbCategoryDto(Guid.NewGuid(), null, "getting-started", "Getting started", null, 10, 1),
                new KbCategoryDto(Guid.NewGuid(), OrbitlyId, "account", "Account", "Sign-in", 20, 3),
            ]);
        return factory;
    }

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/kb/categories", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_the_categories_are_never_read()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync("/kb/categories", Ct);

        html.ShouldNotContain("Getting started");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    // Review Focus 1 of the roles: category delete is Admin only, so a plain agent is never offered the button.
    [Fact]
    public async Task A_plain_agent_sees_the_categories_and_the_create_form_but_no_delete_button()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync("/kb/categories", Ct), Ct);

        page.QuerySelectorAll("tr[data-category]").Length.ShouldBe(2);
        page.QuerySelector("tr[data-category='account']")!.Children[1].TextContent.ShouldBe("Orbitly");
        page.QuerySelector("form.ts-kb-category-create").ShouldNotBeNull();
        page.QuerySelectorAll("button.ts-edit").Length.ShouldBe(2);
        page.QuerySelectorAll("button.ts-delete").ShouldBeEmpty();
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.GetAttribute("aria-current").ShouldBe("page");
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/kb/categories", "/api/products"]);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_admin_sees_a_delete_button_on_every_category()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync("/kb/categories", Ct), Ct);

        page.QuerySelectorAll("button.ts-delete").Length.ShouldBe(2);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
