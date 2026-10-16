using System.Net;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;

namespace TechStrap.Portal.Tests.Features;

public sealed class FeatureBoardHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string ApiPath = "/api/public/products/paperplane/features";
    private static object Summary => new { id = Guid.Parse("1a938e34-1eb3-42ec-838d-238d4adca871"), slug = "dark-mode", title = "Dark mode <script>", progressStatus = "InProgress", updatedAt = "2026-10-05T09:30:00Z" };
    private static object Page(int page = 1, int total = 1) => new { items = new[] { Summary }, page, pageSize = 25, totalCount = total };
    private static PortalFactory Factory()
    {
        var factory = FormTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, ApiPath, Page());
        factory.Api.OnJson(HttpMethod.Get, ApiPath + "/dark-mode", new { id = Guid.NewGuid(), slug = "dark-mode", title = "Dark mode <script>", descriptionHtml = "<h1>Appearance</h1><p>Choose <em>dark</em> colors.</p>", progressStatus = "InProgress", updatedAt = "2026-10-05T09:30:00Z" });
        return factory;
    }

    [Fact]
    public async Task Board_lists_public_features_and_preserves_search_and_status_in_paging()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, ApiPath, Page(total: 26));
        using var client = FormTestKit.Client(factory);
        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/features?q=dark%20%26%20light&status=InProgress", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Features"]);
        dom.QuerySelector("a[href='/p/paperplane/features/dark-mode']")!.TextContent.ShouldBe("Dark mode <script>");
        dom.QuerySelector("a[href='/p/paperplane/contact?type=FeatureRequest']").ShouldNotBeNull();
        dom.QuerySelector("a[href='/p/paperplane/features?q=dark%20%26%20light&status=InProgress&page=2']").ShouldNotBeNull();
        var call = factory.Api.Requests.Single(r => r.Path == ApiPath);
        call.Query.ShouldContain("q=dark%20%26%20light");
        call.Query.ShouldContain("status=InProgress");
        dom.QuerySelector("main")!.TextContent.ShouldContain("In progress");
    }

    [Fact]
    public async Task Detail_uses_sanitized_body_encoded_title_canonical_and_the_same_product_skin()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);
        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/features/dark-mode", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Dark mode <script>"]);
        dom.QuerySelector("article em")!.TextContent.ShouldBe("dark");
        dom.QuerySelector("article h2")!.TextContent.ShouldBe("Appearance");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/features/dark-mode");
        dom.QuerySelector(".ts-product-name")!.TextContent.ShouldBe("Paperplane");
    }

    [Fact]
    public async Task Unpublishing_is_visible_on_the_next_read_and_resets_the_product_theme()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);
        using var first = await client.GetAsync("/p/paperplane/features/dark-mode", Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.CacheControl!.NoStore.ShouldBeTrue();
        factory.Api.OnProblem(HttpMethod.Get, ApiPath + "/dark-mode", HttpStatusCode.NotFound, "feature-not-found", "Private detail");
        var (second, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/features/dark-mode", Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("Private detail");
        dom.QuerySelector(".ts-product-name").ShouldBeNull();
        factory.Api.Count(HttpMethod.Get, ApiPath + "/dark-mode").ShouldBe(2);
    }

    [Fact]
    public async Task Empty_board_keeps_submission_action_and_API_failure_shows_calm_status()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, ApiPath, new { items = Array.Empty<object>(), page = 1, pageSize = 25, totalCount = 0 });
        using var client = FormTestKit.Client(factory);
        var (empty, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/features", Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        dom.QuerySelector("main")!.TextContent.ShouldContain("No features");
        dom.QuerySelector("a[href='/p/paperplane/contact?type=FeatureRequest']").ShouldNotBeNull();
        factory.Api.OnStatus(HttpMethod.Get, ApiPath, HttpStatusCode.TooManyRequests);
        var (busy, html, _) = await KbTestKit.GetAsync(client, "/p/paperplane/features", Ct);
        busy.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        html.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task Product_host_serves_clean_feature_routes_and_redirects_prefixed_routes()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("paperplane", "Paperplane", "support.paperplane.test") });
        using var client = FormTestKit.Client(factory);
        client.DefaultRequestHeaders.Host = "support.paperplane.test";
        var (response, _, dom) = await KbTestKit.GetAsync(client, "/features/dark-mode", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe("https://support.paperplane.test/features/dark-mode");
        dom.QuerySelector("a[href='/features']").ShouldNotBeNull();
        dom.QuerySelector("a[href='/contact?type=FeatureRequest']").ShouldNotBeNull();
        using var redirect = await client.GetAsync("/p/paperplane/features/dark-mode", Ct);
        redirect.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        redirect.Headers.Location!.ToString().ShouldBe("https://support.paperplane.test/features/dark-mode");
    }

    [Fact]
    public async Task Another_products_missing_feature_cannot_reveal_the_original_product()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/other", FormTestKit.Product() with { Key = "other", DisplayName = "Other" });
        using var client = FormTestKit.Client(factory);
        var (response, html, _) = await KbTestKit.GetAsync(client, "/p/other/features/dark-mode", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("Dark mode");
        factory.Api.Count(HttpMethod.Get, ApiPath + "/dark-mode").ShouldBe(0);
    }
}
