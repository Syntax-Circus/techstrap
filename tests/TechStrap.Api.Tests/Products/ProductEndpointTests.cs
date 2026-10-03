using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Products;

public sealed class ProductEndpointTests(TestPostgres postgres)
{
    private static readonly ProductBrandingRequest Branding = new("Orbitly", null, "#7c3aed", null, null);

    private static async Task SignInAsync(HttpClient client) =>
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    private static async Task<ProductDto> CreateAsync(HttpClient admin, string key, string prefix)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, prefix, null), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin, HttpClient Agent)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        return (factory, database, admin, agent);
    }

    [Fact]
    public async Task Create_returns_201_with_a_location_and_the_product_can_be_read()
    {
        var (factory, database, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;

        using var response = await admin.PostAsJsonAsync(
            "/api/products", new CreateProductRequest("orbitly", "Orbitly", "ORB", Branding), TestContext.Current.CancellationToken);
        var created = (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/products/{created.Id}");
        var read = await agent.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}", TestContext.Current.CancellationToken);
        read!.Branding.AccentColour.ShouldBe("#7C3AED");
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'ProductCreated'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_duplicate_key_is_409()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        await CreateAsync(admin, "orbitly", "ORB");

        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("orbitly", "Other", "OTH", null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_malformed_accent_is_400_with_an_accent_field_error()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;

        using var response = await admin.PostAsJsonAsync(
            "/api/products", new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "purple", null, null)), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("accent-colour");
    }

    [Fact]
    public async Task A_stale_version_is_409_and_the_returned_version_allows_the_next_update()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var created = await CreateAsync(admin, "orbitly", "ORB");

        using var first = await admin.PutAsJsonAsync(
            $"/api/products/{created.Id}", new UpdateProductRequest("Orbitly Cloud", Branding, true, created.Version), TestContext.Current.CancellationToken);
        var updated = (await first.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
        using var stale = await admin.PutAsJsonAsync(
            $"/api/products/{created.Id}", new UpdateProductRequest("Orbitly Stale", Branding, true, created.Version), TestContext.Current.CancellationToken);
        using var next = await admin.PutAsJsonAsync(
            $"/api/products/{created.Id}", new UpdateProductRequest("Orbitly Next", Branding, true, updated.Version), TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_agent_sees_active_products_but_cannot_create_one()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var active = await CreateAsync(admin, "orbitly", "ORB");
        var hidden = await CreateAsync(admin, "hidden", "HID");
        using var deactivate = await admin.PutAsJsonAsync(
            $"/api/products/{hidden.Id}", new UpdateProductRequest(hidden.Name, Branding, false, hidden.Version), TestContext.Current.CancellationToken);
        deactivate.EnsureSuccessStatusCode();

        var list = (await agent.GetFromJsonAsync<List<ProductDto>>("/api/products", TestContext.Current.CancellationToken))!;
        using var post = await agent.PostAsJsonAsync("/api/products", new CreateProductRequest("third", "Third", "THR", null), TestContext.Current.CancellationToken);

        list.Select(product => product.Id).ShouldBe([active.Id]);
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Notification_preferences_persist_through_the_api()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var first = await CreateAsync(admin, "orbitly", "ORB");
        var second = await CreateAsync(admin, "second", "SEC");

        using var saved = await agent.PutAsJsonAsync(
            "/api/agents/me/notification-preferences",
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(first.Id, true)]),
            TestContext.Current.CancellationToken);
        var preferences = (await agent.GetFromJsonAsync<List<NotificationPreferenceDto>>("/api/agents/me/notification-preferences", TestContext.Current.CancellationToken))!;
        using var unknown = await agent.PutAsJsonAsync(
            "/api/agents/me/notification-preferences",
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)]),
            TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        preferences.Single(p => p.ProductId == first.Id).NotifyNewTicket.ShouldBeTrue();
        preferences.Single(p => p.ProductId == second.Id).NotifyNewTicket.ShouldBeFalse();
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("preferences[0].productId");
    }
}
