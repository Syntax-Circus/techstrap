using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Products;

/// <summary>POST and DELETE <c>api/products/{id}/logo</c> against a real database and a temporary Local storage root (D-052).</summary>
public sealed class ProductLogoEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];
    private static readonly byte[] Gif = [.. "GIF89a"u8.ToArray(), .. new byte[20]];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBPVP8 "u8.ToArray(), .. new byte[20]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-logos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync(bool kestrel = false)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["Storage:Local:RootPath"] = _storage };
        var factory = new ApiFactory(settings: settings);
        if (kestrel)
        {
            factory.UseKestrel(0); // TestServer has no IHttpMaxRequestBodySizeFeature, so RequestSizeLimit needs the real server.
        }

        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    private static async Task<Guid> CreateProductAsync(HttpClient admin, string key)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, key[..3].ToUpperInvariant(), null, null), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!.Id;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, ProductLogoLimits.FieldName, fileName } };
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static string NameOf(JsonElement root)
    {
        var url = root.GetProperty("branding").GetProperty("uploadedLogoUrl").GetString()!;
        return url[(url.LastIndexOf('/') + 1)..];
    }

    [Fact(Timeout = 120_000)]
    public async Task An_admin_uploads_replaces_and_removes_a_logo_and_the_files_follow()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var id = await CreateProductAsync(admin, "orbitly");

        using var first = await admin.PostAsync($"/api/products/{id}/logo", Form(Png, "logo.png", "image/png"), TestContext.Current.CancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var firstUrl = Json(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).GetProperty("branding").GetProperty("uploadedLogoUrl").GetString()!;
        firstUrl.ShouldStartWith("https://api.test/product-logos/"); // HostFactory sets TECHSTRAP_API_PUBLIC_URL
        var firstName = firstUrl[(firstUrl.LastIndexOf('/') + 1)..];
        File.Exists(Path.Combine(_storage, "product-logos", firstName)).ShouldBeTrue();
        (await database.ScalarAsync<string>("SELECT uploaded_logo FROM products WHERE key = 'orbitly'")).ShouldBe(firstName);

        using var second = await admin.PostAsync($"/api/products/{id}/logo", Form(Webp, "logo.webp", "image/webp"), TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondName = NameOf(Json(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        File.Exists(Path.Combine(_storage, "product-logos", firstName)).ShouldBeFalse("the replaced file is deleted after the commit");
        File.Exists(Path.Combine(_storage, "product-logos", secondName)).ShouldBeTrue();

        using var anonymous = factory.CreateClient();
        using var served = await anonymous.GetAsync($"/product-logos/{secondName}", TestContext.Current.CancellationToken);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");

        using var removed = await admin.DeleteAsync($"/api/products/{id}/logo", TestContext.Current.CancellationToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        Json(await removed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).GetProperty("branding").GetProperty("uploadedLogoUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        File.Exists(Path.Combine(_storage, "product-logos", secondName)).ShouldBeFalse();
        (await database.ScalarAsync<bool>("SELECT uploaded_logo IS NULL FROM products WHERE key = 'orbitly'")).ShouldBeTrue();
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE payload::text LIKE '%uploadedLogo%'")).ShouldBe(3);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_svg_a_gif_and_a_missing_file_are_400s_and_an_agent_is_403()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var id = await CreateProductAsync(admin, "orbitly");

        using var svg = await admin.PostAsync($"/api/products/{id}/logo", Form("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "logo.svg", "image/svg+xml"), TestContext.Current.CancellationToken);
        svg.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await svg.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("product-logo-type-not-allowed");
        using var gif = await admin.PostAsync($"/api/products/{id}/logo", Form(Gif, "logo.gif", "image/gif"), TestContext.Current.CancellationToken);
        gif.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var empty = await admin.PostAsync($"/api/products/{id}/logo", new MultipartFormDataContent { { new StringContent("x"), "other" } }, TestContext.Current.CancellationToken);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("file-required");

        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var forbidden = await agent.PostAsync($"/api/products/{id}/logo", Form(Png, "logo.png", "image/png"), TestContext.Current.CancellationToken);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var unknown = await admin.DeleteAsync($"/api/products/{Guid.NewGuid()}/logo", TestContext.Current.CancellationToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_body_far_over_the_limit_is_a_413_before_the_handler_runs()
    {
        var (factory, _, admin) = await StartAsync(kestrel: true);
        await using var _ = factory;
        using var __ = admin;
        var id = await CreateProductAsync(admin, "orbitly");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{id}/logo") { Content = Form(new byte[(int)ProductLogoRequestLimits.FormBytes + 1024], "big.png", "image/png") };

        // Expect: 100-continue lets Kestrel reject on Content-Length before the client streams the whole body into a closing socket.
        request.Headers.ExpectContinue = true;
        using var response = await admin.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        Directory.Exists(Path.Combine(_storage, "product-logos")).ShouldBeFalse();
    }
}
