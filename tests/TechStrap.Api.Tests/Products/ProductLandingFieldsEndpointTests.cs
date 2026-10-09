using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Products;

public sealed class ProductLandingFieldsEndpointTests(TestPostgres postgres)
{
    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, CreateProductRequest request) =>
        admin.PostAsJsonAsync("/api/products", request, TestContext.Current.CancellationToken);

    // A raw JSON body: the typed record always writes listedOnLanding, so only a hand-written body can omit the property.
    private static Task<HttpResponseMessage> PutRawAsync(HttpClient admin, Guid id, string json) =>
        admin.PutAsync($"/api/products/{id}", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    [Fact(Timeout = 120_000)]
    public async Task Create_update_and_the_public_endpoints_carry_the_tagline_and_the_listed_flag()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var created = await PostAsync(admin, new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, null, null, null, "  Tickets for the app.  "), null, false));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        dto.RootElement.GetProperty("listedOnLanding").GetBoolean().ShouldBeFalse();
        dto.RootElement.GetProperty("branding").GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
        (await database.ScalarAsync<bool>("SELECT listed_on_landing FROM products WHERE key = 'orbitly'")).ShouldBeFalse();

        // A 0.2.0 client omits the property: the flag stays false. (Branding is replaced as a whole, so the PUT repeats the tagline.)
        var id = dto.RootElement.GetProperty("id").GetGuid();
        var version = dto.RootElement.GetProperty("version").GetUInt32();
        using var put = await PutRawAsync(admin, id, "{\"name\":\"Orbitly\",\"branding\":{\"displayName\":\"Orbitly\",\"tagline\":\"Tickets for the app.\"},\"isActive\":true,\"version\":" + version + "}");
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.ScalarAsync<bool>("SELECT listed_on_landing FROM products WHERE key = 'orbitly'")).ShouldBeFalse();

        using var anonymous = factory.CreateClient();
        using var list = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products", TestContext.Current.CancellationToken));
        var row = list.RootElement.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "orbitly");
        row.GetProperty("listedOnLanding").GetBoolean().ShouldBeFalse();
        row.GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
        row.GetProperty("accentColour").GetString().ShouldBe("#1F6FEB");
        using var one = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products/orbitly", TestContext.Current.CancellationToken));
        one.RootElement.GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_tagline_with_a_line_break_is_a_400_on_the_tagline_field()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        using var response = await PostAsync(admin, new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, null, null, null, "two\nlines")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("errorCodes").GetProperty("tagline").EnumerateArray().Select(code => code.GetString()).ShouldBe(["tagline-invalid"]);
    }
}
