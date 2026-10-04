using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Customer;

public sealed class CustomerReplyEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-customer-reply-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, CustomerSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        return (factory, database, await CustomerTestData.SeedAsync(factory, Ct));
    }

    private static MultipartFormDataContent Form(string body)
    {
        var form = new MultipartFormDataContent { { new StringContent(body), "body" } };
        return form;
    }

    private static void AddFile(MultipartFormDataContent form, byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "attachments", fileName);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? token, MultipartFormDataContent form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/ticket/replies") { Content = form };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
        }

        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task A_customer_replies_with_a_png_and_gets_201()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form("Thanks, still failing <script>alert(1)</script>");
        AddFile(form, Png, "pixel.png", "image/png");

        using var response = await PostAsync(client, seed.ValidToken, form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = (await response.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!;
        body.ShouldSatisfyAllConditions(
            b => b.TicketNumber.ShouldBe(seed.Number),
            b => b.FollowUpCreated.ShouldBeFalse(),
            b => b.FollowUpViewUrl.ShouldBeNull());
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM messages WHERE id = '{body.MessageId}' AND author_type = 'Requester' AND body NOT LIKE '%<script%'")).ShouldBe(1);
        (await database.ScalarAsync<long>($"SELECT count(*) FROM attachments WHERE message_id = '{body.MessageId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_reply_on_a_closed_ticket_returns_the_follow_up_link()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form("It broke again");

        using var response = await PostAsync(client, seed.ClosedToken, form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!;
        body.FollowUpCreated.ShouldBeTrue();
        body.TicketNumber.ShouldNotBe(seed.ClosedNumber);
        body.FollowUpViewUrl.ShouldNotBeNull().ShouldStartWith("https://help.test/t/");
        (await database.ScalarAsync<long>($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{seed.ClosedTicketId}'")).ShouldBe(1);

        // The link works as a customer token for the follow-up.
        var token = body.FollowUpViewUrl["https://help.test/t/".Length..];
        using var view = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        view.Headers.Add(HeaderNames.TicketToken, token);
        using var viewed = await client.SendAsync(view, Ct);
        viewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewed.Content.ReadFromJsonAsync<CustomerTicketDto>(Ct))!.Number.ShouldBe(body.TicketNumber);
    }

    [Fact]
    public async Task An_executable_attachment_is_400()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var form = Form("See attached");
        AddFile(form, [0x4D, 0x5A, 0, 1, 2, 3, 4, 5], "invoice.pdf", "application/pdf");

        using var response = await PostAsync(client, seed.ValidToken, form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("attachment-type-not-allowed");
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments WHERE file_name = 'invoice.pdf'")).ShouldBe(0);
        (Directory.Exists(_storage) ? Directory.GetFiles(_storage, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_token_is_404_with_the_uniform_body()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var good = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket") { Headers = { { HeaderNames.TicketToken, "garbage" } } }, Ct);
        using var form = Form("Hello");

        using var response = await PostAsync(client, seed.RevokedToken, form);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        // Byte for byte the GET 404, apart from the echoed request path.
        (await response.Content.ReadAsStringAsync(Ct)).Replace("/replies", "").ShouldBe(await good.Content.ReadAsStringAsync(Ct));
    }
}
