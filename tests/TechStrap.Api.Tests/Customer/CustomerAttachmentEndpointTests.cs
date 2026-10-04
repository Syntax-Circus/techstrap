using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Storage;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Customer;

public sealed class CustomerAttachmentEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Bytes = "0123456789"u8.ToArray(); // the seed declares size 10

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-customer-attachment-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private async Task<(ApiFactory Factory, CustomerSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["Storage:Local:RootPath"] = _storage };
        var factory = new ApiFactory(settings: settings);
        var seed = await CustomerTestData.SeedAsync(factory, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IStorageProvider>();
        foreach (var key in new[] { "storage/public", "storage/internal", "storage/other" })
        {
            await using var content = new MemoryStream(Bytes);
            await provider.StoreAsync(new StoreObjectRequest(key, content, "text/plain"), Ct);
        }

        return (factory, seed);
    }

    private static HttpRequestMessage Get(Guid id, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/customer/attachments/{id}");
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
        }

        return request;
    }

    [Fact]
    public async Task The_customer_downloads_a_public_attachment_with_safe_headers_and_sandbox()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var request = Get(seed.PublicAttachmentId, seed.ValidToken);

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Bytes);
        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileNameStar.ShouldBe("log.txt");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        var csp = string.Join("; ", response.Headers.GetValues("Content-Security-Policy"));
        csp.ShouldContain("sandbox");
        csp.ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Other_ticket_and_internal_note_attachments_look_like_missing_ones()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var ignored = new[] { "Date", "X-Correlation-Id", "X-Request-Id" };

        var shapes = new List<string>();
        foreach (var (id, token) in new (Guid, string?)[]
        {
            (seed.InternalAttachmentId, seed.ValidToken),
            (seed.OtherTicketAttachmentId, seed.ValidToken),
            (Guid.CreateVersion7(), seed.ValidToken),
            (seed.PublicAttachmentId, "garbage"),
            (seed.PublicAttachmentId, null),
            (seed.PublicAttachmentId, seed.RevokedToken),
        })
        {
            using var request = Get(id, token);
            using var response = await client.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !ignored.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
            var body = (await response.Content.ReadAsStringAsync(Ct)).Replace(id.ToString(), "ID");
            shapes.Add($"{(int)response.StatusCode}|{body}|{string.Join(";", headers)}");
        }

        shapes.Distinct().Count().ShouldBe(1, string.Join("\n", shapes));
        shapes[0].ShouldContain("not-found");
        shapes[0].ShouldContain("no-store");
        shapes[0].ShouldNotContain("sandbox");
    }

    [Fact]
    public async Task The_agent_download_still_has_sandbox_after_the_middleware_move()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        await using var factory = new ApiFactory(settings: settings);
        var seed = await TicketTestData.SeedAsync(factory, Ct);
        using var sam = TicketTestData.AgentClient(factory, "sam");
        using var form = new MultipartFormDataContent { { new StringContent("See attached"), "body" } };
        var file = new ByteArrayContent(Bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        file.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=attachments; filename*=UTF-8''a.txt");
        form.Add(file);
        using var reply = await sam.PostAsync($"/api/tickets/{seed.Tickets[0].Id}/replies", form, Ct);
        reply.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await reply.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct))!.Message.Attachments.ShouldHaveSingleItem().Id;

        using var response = await sam.GetAsync($"/api/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csp = string.Join("; ", response.Headers.GetValues("Content-Security-Policy"));
        csp.ShouldContain("sandbox");
        csp.ShouldContain("frame-ancestors 'none'");
    }
}
