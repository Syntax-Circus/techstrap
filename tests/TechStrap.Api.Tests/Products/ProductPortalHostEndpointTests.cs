using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    // The Api test host's TECHSTRAP_PORTAL_PUBLIC_URL is https://portal.test (HostFactory), so portal.test is the reserved host.
    [Fact]
    public async Task The_portals_own_host_is_400_on_create_and_on_update_with_a_reserved_code()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        using var create = await PostAsync(admin, "orbitly", "ORB", "Portal.Test");
        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var createBody = JsonDocument.Parse(await create.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        createBody.RootElement.GetProperty("errorCodes").GetProperty("portal-host").EnumerateArray().Select(code => code.GetString()).ShouldBe(["product-host-reserved"]);

        using var made = await PostAsync(admin, "paperplane", "PAP", null);
        var product = (await made.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
        using var update = await admin.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductRequest(product.Name, Branding, true, product.Version, "portal.test"), TestContext.Current.CancellationToken);
        update.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var updateBody = JsonDocument.Parse(await update.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        updateBody.RootElement.GetProperty("errorCodes").GetProperty("portal-host").EnumerateArray().Select(code => code.GetString()).ShouldBe(["product-host-reserved"]);
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

    // Raw JSON bodies: the typed record always writes portalHost, so only a hand-written body can omit the property.
    private static Task<HttpResponseMessage> PutRawAsync(HttpClient admin, ProductDto product, string portalHostJson)
    {
        var json = $$"""{"name":"Orbitly Cloud","branding":{"displayName":"Orbitly","accentColour":"#7c3aed"},"isActive":true,"version":{{product.Version}}{{portalHostJson}}}""";
        return admin.PutAsync($"/api/products/{product.Id}", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
    }

    private static async Task<ProductDto> CreateWithHostAsync(HttpClient admin)
    {
        using var created = await PostAsync(admin, "orbitly", "ORB", "support.example.com");
        return (await created.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<ProductDto> GetAsync(HttpClient admin, Guid id) =>
        (await admin.GetFromJsonAsync<ProductDto>($"/api/products/{id}", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Update_keeps_the_own_host_without_conflict()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var product = await CreateWithHostAsync(admin);

        using var same = await PutRawAsync(admin, product, ",\"portalHost\":\"Support.Example.com\"");

        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, product.Id)).PortalHost.ShouldBe("support.example.com");
    }

    [Fact]
    public async Task Update_with_a_null_portal_host_keeps_the_host()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var product = await CreateWithHostAsync(admin);

        using var response = await PutRawAsync(admin, product, ",\"portalHost\":null");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, product.Id)).PortalHost.ShouldBe("support.example.com");
    }

    [Fact]
    public async Task Update_without_the_portal_host_property_keeps_the_host()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var product = await CreateWithHostAsync(admin);

        using var response = await PutRawAsync(admin, product, "");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, product.Id)).PortalHost.ShouldBe("support.example.com");
    }

    [Fact]
    public async Task Update_with_an_empty_portal_host_clears_it()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var product = await CreateWithHostAsync(admin);

        using var response = await PutRawAsync(admin, product, ",\"portalHost\":\"\"");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, product.Id)).PortalHost.ShouldBeNull();
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
