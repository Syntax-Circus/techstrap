using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Products;

public sealed class ProductPortalHostEndpointTests(TestPostgres postgres)
{
    private static readonly ProductBrandingRequest Branding = new("Orbitly", null, "#7c3aed", null, null);

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, string key, string prefix, string? host) =>
        admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, prefix, null, host), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Create_with_a_mixed_case_host_returns_201_and_stores_and_returns_it_lower_case()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        using var response = await PostAsync(admin, "orbitly", "ORB", "Support.DragonPoop.COM");
        var created = (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.PortalHost.ShouldBe("support.dragonpoop.com");
        (await database.ScalarAsync<string>("SELECT portal_host FROM products WHERE key = 'orbitly'")).ShouldBe("support.dragonpoop.com");
    }

    [Fact]
    public async Task An_invalid_host_is_400_with_a_portal_host_error_code()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        using var response = await PostAsync(admin, "orbitly", "ORB", "https://support.example.com");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("errorCodes").GetProperty("portal-host").EnumerateArray().Select(code => code.GetString()).ShouldBe(["product-host-invalid"]);
    }

    [Fact]
    public async Task Hosts_differing_only_by_case_collide()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var first = await PostAsync(admin, "orbitly", "ORB", "support.example.com");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await PostAsync(admin, "paperplane", "PAP", "SUPPORT.Example.COM");

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("product-host-taken");
    }

    [Fact]
    public async Task Update_keeps_the_own_host_without_conflict_and_null_clears_it()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var created = await PostAsync(admin, "orbitly", "ORB", "support.example.com");
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;

        using var same = await admin.PutAsJsonAsync(
            $"/api/products/{product.Id}", new UpdateProductRequest("Orbitly Cloud", Branding, true, product.Version, "Support.Example.com"), TestContext.Current.CancellationToken);
        var renamed = (await same.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
        using var cleared = await admin.PutAsJsonAsync(
            $"/api/products/{product.Id}", new UpdateProductRequest("Orbitly Cloud", Branding, true, renamed.Version, null), TestContext.Current.CancellationToken);
        var after = (await cleared.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;

        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        renamed.PortalHost.ShouldBe("support.example.com");
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.PortalHost.ShouldBeNull();
    }

    [Fact]
    public async Task The_public_endpoints_return_the_portal_host()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        (await PostAsync(admin, "orbitly", "ORB", "support.example.com")).EnsureSuccessStatusCode();
        (await PostAsync(admin, "paperplane", "PAP", null)).EnsureSuccessStatusCode();
        using var anonymous = factory.CreateClient();

        var one = await anonymous.GetStringAsync("/api/public/products/orbitly", TestContext.Current.CancellationToken);
        var list = await anonymous.GetStringAsync("/api/public/products", TestContext.Current.CancellationToken);

        using var oneBody = JsonDocument.Parse(one);
        oneBody.RootElement.GetProperty("portalHost").GetString().ShouldBe("support.example.com");
        using var listBody = JsonDocument.Parse(list);
        listBody.RootElement.EnumerateArray().Select(item => item.GetProperty("portalHost").GetString()).ShouldBe(["support.example.com", null]);
    }
}
