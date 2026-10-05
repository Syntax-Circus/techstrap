using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>Every screen has one <c>h1</c> (UX-BRIEF-admin, accessibility). The two pages that are a brand window and nothing else, the 404 and the sign-in landing, used to have none.</summary>
public sealed class HeadingHostTests
{
    private static async Task<AngleSharp.Dom.IDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_404_page_has_exactly_one_h1_and_it_is_the_window_heading()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // The 404 page is a static page: no circuit and no API call, so there is no Authorization header to check (AssertEveryCallBore fails on zero calls), as in NotFoundHostTests.
        var page = await GetAsync(client, "/no-such-page");

        page.QuerySelectorAll("h1").Select(h => h.TextContent).ShouldBe(["This page fell out of its strap."]);
        page.QuerySelectorAll(".ts-window-body h2").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sign_in_landing_page_has_exactly_one_h1_and_it_is_the_window_heading()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await GetAsync(client, "/signin?returnUrl=%2F");

        page.QuerySelectorAll("h1").Count.ShouldBe(1);
        page.QuerySelector("h1")!.TextContent.ShouldNotBeNullOrWhiteSpace();
        page.QuerySelectorAll("h2").ShouldBeEmpty();
    }
}
