using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Skins;

namespace TechStrap.Api.Tests.Products;

public sealed class ProductSkinEndpointTests(TestPostgres postgres)
{
    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    // A raw JSON body: the typed record always writes skin, so only a hand-written body can omit it or send unmapped members.
    private static Task<HttpResponseMessage> PutRawAsync(HttpClient admin, Guid id, string json) =>
        admin.PutAsync($"/api/products/{id}", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private static string Body(uint version, string? skin) =>
        "{\"name\":\"Orbitly\",\"branding\":{\"displayName\":\"Orbitly\"},\"isActive\":true,\"version\":" + version + (skin is null ? string.Empty : ",\"skin\":" + skin) + "}";

    private static async Task<(Guid Id, uint Version)> CreateAsync(HttpClient admin, ProductSkin? skin)
    {
        using var created = await admin.PostAsJsonAsync(
            "/api/products", new CreateProductRequest("orbitly", "Orbitly", "ORB", null, Skin: skin), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (dto.RootElement.GetProperty("id").GetGuid(), dto.RootElement.GetProperty("version").GetUInt32());
    }

    [Fact(Timeout = 120_000)]
    public async Task A_skin_set_on_create_is_stored_and_carried_by_the_public_product_and_list()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        await CreateAsync(admin, new ProductSkin(Pack: "paper", Brand: "#112233"));

        (await database.ScalarAsync<string>("SELECT skin FROM products WHERE key = 'orbitly'")).ShouldBe("{\"pack\":\"paper\",\"brand\":\"#112233\"}");
        using var anonymous = factory.CreateClient();
        using var one = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products/orbitly", TestContext.Current.CancellationToken));
        one.RootElement.GetProperty("skin").GetProperty("pack").GetString().ShouldBe("paper");
        using var list = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products", TestContext.Current.CancellationToken));
        list.RootElement.EnumerateArray().Single().GetProperty("skin").GetProperty("brand").GetString().ShouldBe("#112233");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_put_without_skin_keeps_it_and_an_empty_skin_clears_it()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var (id, version) = await CreateAsync(admin, new ProductSkin(Pack: "slate"));

        using var omitted = await PutRawAsync(admin, id, Body(version, null));
        omitted.StatusCode.ShouldBe(HttpStatusCode.OK, await omitted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await database.ScalarAsync<string>("SELECT skin FROM products WHERE key = 'orbitly'")).ShouldBe("{\"pack\":\"slate\"}");

        using var cleared = await PutRawAsync(admin, id, Body(version, "{}"));
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.ScalarAsync<bool>("SELECT skin IS NULL FROM products WHERE key = 'orbitly'")).ShouldBeTrue();
    }

    [Fact(Timeout = 120_000)]
    public async Task A_bad_colour_is_400_with_skin_invalid_on_that_token()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var (id, version) = await CreateAsync(admin, null);

        using var response = await PutRawAsync(admin, id, Body(version, "{\"background\":\"red\"}"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("errorCodes").GetProperty("background").EnumerateArray().Select(code => code.GetString()).ShouldBe(["skin-invalid"]);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_unknown_member_inside_skin_is_rejected_and_nothing_is_stored()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var (id, version) = await CreateAsync(admin, null);

        using var response = await PutRawAsync(admin, id, Body(version, "{\"shine\":\"yes\"}"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await database.ScalarAsync<bool>("SELECT skin IS NULL FROM products WHERE key = 'orbitly'")).ShouldBeTrue();
    }
}
