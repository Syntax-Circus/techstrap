using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class AttachmentDownloadEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-attachment-download-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private async Task<(ApiFactory Factory, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        return (factory, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static async Task<AttachmentDto> ReplyWithAsync(HttpClient client, Guid ticketId, byte[] bytes, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent { { new StringContent("See attached"), "body" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        // RFC 5987 part header: HttpClient refuses a raw quote in a file name, and ASP.NET decodes filename*.
        file.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=attachments; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
        form.Add(file);
        using var response = await client.PostAsync($"/api/tickets/{ticketId}/replies", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct))!;
        return body.Message.Attachments.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_agent_downloads_the_exact_bytes_as_an_attachment_with_nosniff()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var id = (await ReplyWithAsync(sam, seed.Tickets[0].Id, Png, "pixel.png", "image/png")).Id;

        using var response = await sam.GetAsync($"/api/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        response.Content.Headers.ContentLength.ShouldBe(Png.Length);
        response.Headers.GetValues("Content-Security-Policy").ShouldContain("sandbox");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task A_file_name_with_quotes_and_unicode_is_encoded_safely()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var stored = await ReplyWithAsync(sam, seed.Tickets[0].Id, "hello"u8.ToArray(), "re\"port ü.txt", "text/plain");
        stored.FileName.ShouldContain("\"");
        stored.FileName.ShouldContain("ü");

        using var response = await sam.GetAsync($"/api/attachments/{stored.Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileNameStar.ShouldBe(stored.FileName);
        disposition.FileName.ShouldBe("re_port _.txt");
    }

    [Fact]
    public async Task An_unknown_attachment_is_404()
    {
        var (factory, _) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        using var response = await sam.GetAsync($"/api/attachments/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_anonymous_caller_is_401()
    {
        var (factory, _) = await StartAsync();
        await using var _f = factory;
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync($"/api/attachments/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
