using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class PublicIntakeEndpointTests(TestPostgres postgres) : IAsyncLifetime
{
    private static readonly byte[] _png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-public-intake-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync(bool kestrel = false)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        if (kestrel)
        {
            factory.UseKestrel(0); // TestServer has no IHttpMaxRequestBodySizeFeature, so RequestSizeLimit needs the real server.
        }

        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, database, seed);
    }

    private static MultipartFormDataContent Form(string? website = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" },
            { new StringContent("Ada"), "name" },
            { new StringContent("Help"), "subject" },
            { new StringContent("Please help"), "body" },
        };
        if (website is not null)
        {
            form.Add(new StringContent(website), "website");
        }

        return form;
    }

    private static void AddFile(MultipartFormDataContent form, byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "attachments", fileName);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string key, HttpContent content, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/products/{key}/tickets") { Content = content };
        configure?.Invoke(request);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_anonymous_form_submission_returns_201_without_a_view_url()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();

        using var response = await PostAsync(client, "orbitly", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        body.TicketNumber.ShouldBe("ORB-1");
        body.ViewUrl.ShouldBeNull();
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets WHERE channel = 'Web' AND metadata_trusted = false")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_png_attachment_is_stored_and_recorded()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();
        AddFile(form, _png, "pixel.png", "image/png");

        using var response = await PostAsync(client, "orbitly", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments WHERE content_type = 'image/png'")).ShouldBe(1);
        Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_filled_honeypot_returns_a_plausible_201_and_writes_nothing()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form("https://spam.example");

        using var response = await PostAsync(client, "orbitly", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        body.TicketNumber.ShouldMatch(@"^ORB-\d+$");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
        (await database.ScalarAsync<long>("SELECT count(*) FROM requesters")).ShouldBe(0);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox")).ShouldBe(0);
    }

    [Fact]
    public async Task An_executable_renamed_to_pdf_is_400_attachment_type_not_allowed_and_nothing_is_stored()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();
        AddFile(form, [0x4D, 0x5A, 0, 1, 2, 3, 4, 5], "invoice.pdf", "application/pdf");

        using var response = await PostAsync(client, "orbitly", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("attachment-type-not-allowed");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
        (Directory.Exists(_storage) ? Directory.GetFiles(_storage, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Six_files_is_400_attachments_too_many()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();
        for (var i = 0; i < 6; i++)
        {
            AddFile(form, _png, $"pixel{i}.png", "image/png");
        }

        using var response = await PostAsync(client, "orbitly", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("attachments-too-many");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("dormant")]
    public async Task An_unknown_or_deactivated_product_is_404(string key)
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();

        using var response = await PostAsync(client, key, form);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_form_body_over_the_limit_is_413()
    {
        var (factory, _, _) = await StartAsync(kestrel: true);
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form();
        AddFile(form, new byte[IntakeRequestLimits.FormBodyBytes + 1], "big.bin", "application/octet-stream");

        // Expect: 100-continue lets Kestrel reject on Content-Length before the client streams 25 MiB into a closing socket.
        using var response = await PostAsync(client, "orbitly", form, request => request.Headers.ExpectContinue = true);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!;
        problem.Type.ShouldBe("request-too-large");
    }

    [Fact]
    public async Task A_form_post_ignores_any_api_key_or_bearer_token()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var bearer = TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com");
        using var form = Form();

        using var response = await PostAsync(client, "orbitly", form, request =>
        {
            request.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets WHERE channel = 'Web' AND metadata_trusted = false")).ShouldBe(1);
    }
}
