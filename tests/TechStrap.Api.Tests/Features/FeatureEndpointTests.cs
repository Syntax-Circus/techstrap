using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Features;

public sealed class FeatureEndpointTests(TestPostgres postgres)
{
    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    private static async Task<Guid> ProductAsync(HttpClient admin, string key)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, key == "orbitly" ? "ORB" : "PPL", null), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<FeatureDto> CreateAsync(HttpClient admin, Guid product, string slug = "dark-mode", string title = "Curated dark mode", string markdown = "A **curated** description.")
    {
        using var response = await admin.PostAsJsonAsync("/api/features", new CreateFeatureRequest(product, slug, title, markdown), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<FeatureDto>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<FeatureDto> PublishAsync(HttpClient admin, FeatureDto feature)
    {
        using var response = await admin.PostAsync($"/api/features/{feature.Id}/publish?version={feature.Version}", null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<FeatureDto>(TestContext.Current.CancellationToken))!;
    }

    [Fact(Timeout = 120_000)]
    public async Task Draft_is_private_publishing_only_exposes_curated_sanitized_content_and_unpublishing_is_immediate()
    {
        var (factory, _, admin) = await StartAsync();
        await using var f = factory;
        using var a = admin;
        using var anonymous = factory.CreateClient();
        var product = await ProductAsync(admin, "orbitly");
        var draft = await CreateAsync(admin, product, markdown: "# Display\n\nA **curated** description.\n\n<script>alert('private')</script>\n\n[bad](javascript:alert(1))");
        draft.PublicationState.ShouldBe("Draft");
        draft.ProgressStatus.ShouldBe("UnderConsideration");
        using var hidden = await anonymous.GetAsync("/api/public/products/orbitly/features/dark-mode", TestContext.Current.CancellationToken);
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features", TestContext.Current.CancellationToken))!.Items.ShouldBeEmpty();

        var published = await PublishAsync(admin, draft);
        using var visible = await anonymous.GetAsync("/api/public/products/orbitly/features/dark-mode", TestContext.Current.CancellationToken);
        visible.StatusCode.ShouldBe(HttpStatusCode.OK);
        visible.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var json = JsonDocument.Parse(await visible.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] { "id", "slug", "title", "descriptionHtml", "progressStatus", "updatedAt" }.Order());
        json.RootElement.GetProperty("title").GetString().ShouldBe("Curated dark mode");
        var body = json.RootElement.GetProperty("descriptionHtml").GetString()!;
        body.ShouldContain("<strong>curated</strong>");
        body.ShouldNotContain("<script");
        body.ShouldNotContain("javascript:");
        using var unpublished = await admin.PostAsync($"/api/features/{published.Id}/unpublish?version={published.Version}", null, TestContext.Current.CancellationToken);
        unpublished.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var gone = await anonymous.GetAsync("/api/public/products/orbitly/features/dark-mode", TestContext.Current.CancellationToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features", TestContext.Current.CancellationToken))!.TotalCount.ShouldBe(0);
    }

    [Fact(Timeout = 120_000)]
    public async Task Public_list_filters_search_and_progress_orders_by_latest_update_and_paginates()
    {
        var (factory, database, admin) = await StartAsync();
        await using var f = factory;
        using var a = admin;
        using var anonymous = factory.CreateClient();
        var product = await ProductAsync(admin, "orbitly");
        var old = await PublishAsync(admin, await CreateAsync(admin, product, "old-dark", "Older dark mode"));
        var newest = await PublishAsync(admin, await CreateAsync(admin, product, "new-dark", "New dark mode"));
        await CreateAsync(admin, product, "draft-dark", "Private dark mode");
        using var updated = await admin.PutAsJsonAsync($"/api/features/{newest.Id}", new UpdateFeatureRequest(newest.Title, newest.DescriptionMarkdown, "InProgress", newest.Version), TestContext.Current.CancellationToken);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        await database.ExecuteAsync($"UPDATE features SET updated_at = '2026-01-01T00:00:00Z' WHERE id = '{old.Id}'; UPDATE features SET updated_at = '2026-02-01T00:00:00Z' WHERE id = '{newest.Id}'");
        var page = (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features?q=dark&pageSize=1", TestContext.Current.CancellationToken))!;
        page.TotalCount.ShouldBe(2);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(newest.Id);
        var second = (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features?q=dark&pageSize=1&page=2", TestContext.Current.CancellationToken))!;
        second.Items.ShouldHaveSingleItem().Id.ShouldBe(old.Id);
        var filtered = (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features?status=InProgress&q=new", TestContext.Current.CancellationToken))!;
        filtered.TotalCount.ShouldBe(1);
        filtered.Items.ShouldHaveSingleItem().Id.ShouldBe(newest.Id);
        var none = (await anonymous.GetFromJsonAsync<PagedResponse<PublicFeatureSummaryDto>>("/api/public/products/orbitly/features?q=unmatched", TestContext.Current.CancellationToken))!;
        none.TotalCount.ShouldBe(0);
    }

    [Fact(Timeout = 120_000)]
    public async Task Missing_unpublished_cross_product_and_inactive_features_have_one_neutral_not_found_response()
    {
        var (factory, database, admin) = await StartAsync();
        await using var f = factory;
        using var a = admin;
        using var anonymous = factory.CreateClient();
        var orbitly = await ProductAsync(admin, "orbitly");
        await ProductAsync(admin, "paperplane");
        await PublishAsync(admin, await CreateAsync(admin, orbitly));
        await CreateAsync(admin, orbitly, "draft");
        var paths = new[] { "/api/public/products/paperplane/features/dark-mode", "/api/public/products/orbitly/features/draft", "/api/public/products/orbitly/features/missing", "/api/public/products/missing/features/dark-mode" };
        var bodies = new List<string>();
        foreach (var path in paths)
        {
            using var response = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            bodies.Add(body.RootElement.GetProperty("detail").GetString()!);
        }
        bodies.Distinct().ShouldHaveSingleItem();
        await database.ExecuteAsync($"UPDATE products SET is_active = false WHERE id = '{orbitly}'");
        foreach (var path in new[] { "/api/public/products/orbitly/features", "/api/public/products/orbitly/features/dark-mode" })
        {
            using var response = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Anonymous_access_cannot_read_private_feature_data_or_mutate_and_stale_agent_versions_conflict()
    {
        var (factory, _, admin) = await StartAsync();
        await using var f = factory;
        using var a = admin;
        using var anonymous = factory.CreateClient();
        var product = await ProductAsync(admin, "orbitly");
        var draft = await CreateAsync(admin, product);
        foreach (var path in new[] { "/api/features", $"/api/features/{draft.Id}", $"/api/features/{draft.Id}/tickets" })
        {
            using var response = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using var create = await anonymous.PostAsJsonAsync("/api/features", new CreateFeatureRequest(product, "forbidden", "Forbidden", "Private"), TestContext.Current.CancellationToken);
        create.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var publish = await anonymous.PostAsync($"/api/features/{draft.Id}/publish?version={draft.Version}", null, TestContext.Current.CancellationToken);
        publish.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var published = await PublishAsync(admin, draft);
        using var stale = await admin.PutAsJsonAsync($"/api/features/{draft.Id}", new UpdateFeatureRequest("Overwritten", "Wrong", "Shipped", draft.Version), TestContext.Current.CancellationToken);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var stalePublish = await admin.PostAsync($"/api/features/{draft.Id}/publish?version={draft.Version}", null, TestContext.Current.CancellationToken);
        stalePublish.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var saved = (await admin.GetFromJsonAsync<FeatureDto>($"/api/features/{draft.Id}", TestContext.Current.CancellationToken))!;
        saved.Version.ShouldBe(published.Version);
        saved.Title.ShouldBe("Curated dark mode");
        saved.ProgressStatus.ShouldBe("UnderConsideration");
    }

    [Fact(Timeout = 120_000)]
    public async Task Several_private_requests_link_to_one_feature_with_product_and_type_guards_until_unlinked()
    {
        var (factory, _, admin) = await StartAsync();
        await using var f = factory;
        using var a = admin;
        var seed = await TicketTestData.SeedAsync(factory, TestContext.Current.CancellationToken);
        var feature = await PublishAsync(admin, await CreateAsync(admin, seed.Orbitly.Id));
        var linked = new List<Guid>();
        foreach (var ticket in new[] { seed.Tickets[0], seed.Tickets[1], seed.Tickets[5] })
        {
            var version = await TicketTestData.VersionAsync(admin, ticket.Id);
            using var type = await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/type", new ChangeTicketTypeRequest("FeatureRequest", version), TestContext.Current.CancellationToken);
            type.StatusCode.ShouldBe(HttpStatusCode.OK);
            version = (await type.Content.ReadFromJsonAsync<TicketStateDto>(TestContext.Current.CancellationToken))!.RowVersion;
            using var link = await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/feature", new LinkTicketFeatureRequest(feature.Id, version), TestContext.Current.CancellationToken);
            link.StatusCode.ShouldBe(HttpStatusCode.OK, await link.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            linked.Add(ticket.Id);
        }
        var requests = (await admin.GetFromJsonAsync<IReadOnlyList<LinkedFeatureTicketDto>>($"/api/features/{feature.Id}/tickets", TestContext.Current.CancellationToken))!;
        requests.Select(t => t.Id).Order().ShouldBe(linked.Order());
        var target = seed.Tickets[0];
        var current = await TicketTestData.VersionAsync(admin, target.Id);
        using var retype = await admin.PutAsJsonAsync($"/api/tickets/{target.Id}/type", new ChangeTicketTypeRequest("Support", current), TestContext.Current.CancellationToken);
        retype.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var move = await admin.PutAsJsonAsync($"/api/tickets/{target.Id}/product", new MoveTicketProductRequest(seed.Paperplane.Id, current), TestContext.Current.CancellationToken);
        move.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var other = seed.Tickets[2];
        var otherVersion = await TicketTestData.VersionAsync(admin, other.Id);
        using var otherType = await admin.PutAsJsonAsync($"/api/tickets/{other.Id}/type", new ChangeTicketTypeRequest("FeatureRequest", otherVersion), TestContext.Current.CancellationToken);
        otherType.StatusCode.ShouldBe(HttpStatusCode.OK);
        otherVersion = (await otherType.Content.ReadFromJsonAsync<TicketStateDto>(TestContext.Current.CancellationToken))!.RowVersion;
        using var crossProduct = await admin.PostAsJsonAsync($"/api/tickets/{other.Id}/feature", new LinkTicketFeatureRequest(feature.Id, otherVersion), TestContext.Current.CancellationToken);
        crossProduct.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var anonymous = factory.CreateClient();
        using var publicResponse = await anonymous.GetAsync("/api/public/products/orbitly/features/dark-mode", TestContext.Current.CancellationToken);
        var publicJson = await publicResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        publicJson.ShouldNotContain("ann@example.com");
        publicJson.ShouldNotContain("Login broken");
        publicJson.ShouldNotContain("Win a prize");
        publicJson.ShouldNotContain("ticket");
        publicJson.ShouldNotContain("count");
        using var unlink = await admin.PostAsJsonAsync($"/api/tickets/{target.Id}/feature", new LinkTicketFeatureRequest(null, current), TestContext.Current.CancellationToken);
        unlink.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterUnlink = (await unlink.Content.ReadFromJsonAsync<TicketStateDto>(TestContext.Current.CancellationToken))!;
        using var retyped = await admin.PutAsJsonAsync($"/api/tickets/{target.Id}/type", new ChangeTicketTypeRequest("Support", afterUnlink.RowVersion), TestContext.Current.CancellationToken);
        retyped.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterType = (await retyped.Content.ReadFromJsonAsync<TicketStateDto>(TestContext.Current.CancellationToken))!;
        using var moved = await admin.PutAsJsonAsync($"/api/tickets/{target.Id}/product", new MoveTicketProductRequest(seed.Paperplane.Id, afterType.RowVersion), TestContext.Current.CancellationToken);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
