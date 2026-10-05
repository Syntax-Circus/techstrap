using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class AgentReplyEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-agent-reply-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        return (factory, database, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static void AddFile(MultipartFormDataContent form, byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "attachments", fileName);
    }

    [Fact]
    public async Task An_agent_replies_with_markdown_and_a_png_and_gets_201_with_the_message_and_new_row_version()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[0];
        using var form = new MultipartFormDataContent { { new StringContent("Hi **Ann**, try again <script>alert(1)</script>"), "body" } };
        AddFile(form, Png, "pixel.png", "image/png");

        using var response = await sam.PostAsync($"/api/tickets/{ticket.Id}/replies", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct))!;
        body.Message.ShouldSatisfyAllConditions(
            m => m.AuthorName.ShouldBe("Sam"),
            m => m.Visibility.ShouldBe("Public"),
            m => m.BodyHtml.ShouldContain("<strong>Ann</strong>"),
            m => m.BodyHtml.ShouldNotContain("<script"),
            m => m.Attachments.ShouldHaveSingleItem().FileName.ShouldBe("pixel.png"));
        body.Ticket.ShouldSatisfyAllConditions(
            t => t.Status.ShouldBe("Pending"),
            t => t.RowVersion.ShouldNotBe(ticket.Version));
        (await database.ScalarAsync<long>($"SELECT count(*) FROM attachments WHERE message_id = '{body.Message.Id}'")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'agent-reply'")).ShouldBe(1);
    }

    [Fact]
    public async Task Review_Focus_5_a_reply_links_only_published_articles_of_the_tickets_product_and_the_email_carries_their_portal_links()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[0];
        var own = ticket.ProductId == seed.Orbitly.Id ? seed.Orbitly : seed.Paperplane;
        var other = ticket.ProductId == seed.Orbitly.Id ? seed.Paperplane : seed.Orbitly;
        async Task<Guid> ArticleAsync(Guid? productId, string categorySlug, string slug, bool publish)
        {
            using var category = await sam.PostAsJsonAsync("/api/kb/categories", new CreateKbCategoryRequest(productId, categorySlug, categorySlug, null, 1), Ct);
            Guid categoryId;
            if (category.StatusCode == HttpStatusCode.Created)
            {
                categoryId = (await category.Content.ReadFromJsonAsync<KbCategoryDto>(Ct))!.Id;
            }
            else
            {
                categoryId = (await sam.GetFromJsonAsync<List<KbCategoryDto>>("/api/kb/categories", Ct))!.Single(c => c.Slug == categorySlug).Id;
            }

            using var created = await sam.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, null, "Steps."), Ct);
            var article = (await created.Content.ReadFromJsonAsync<KbArticleDto>(Ct))!;
            if (publish)
            {
                (await sam.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
            }

            return article.Id;
        }

        var ownPublished = await ArticleAsync(own.Id, "own-cat", "own-published", publish: true);
        var ownDraft = await ArticleAsync(own.Id, "own-cat", "own-draft", publish: false);
        var otherPublished = await ArticleAsync(other.Id, "other-cat", "other-published", publish: true);
        var sharedPublished = await ArticleAsync(null, "general", "shared-published", publish: true);

        async Task<HttpResponseMessage> ReplyAsync(params Guid[] ids)
        {
            using var form = new MultipartFormDataContent { { new StringContent("See these"), "body" } };
            foreach (var id in ids)
            {
                form.Add(new StringContent(id.ToString()), "linkedArticleIds");
            }

            return await sam.PostAsync($"/api/tickets/{ticket.Id}/replies", form, Ct);
        }

        using var draft = await ReplyAsync(ownPublished, ownDraft);
        using var foreign = await ReplyAsync(sharedPublished, otherPublished);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'agent-reply'")).ShouldBe(0);
        using var accepted = await ReplyAsync(ownPublished, sharedPublished);

        draft.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await draft.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-linkable");
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await foreign.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-linkable");
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await database.ScalarAsync<string>("SELECT payload::text FROM email_outbox WHERE kind = 'agent-reply'");
        payload.ShouldContain($"https://help.test/p/{own.Key}/kb/own-cat/own-published");
        payload.ShouldContain($"https://help.test/p/{own.Key}/kb/general/shared-published");
        payload.ShouldNotContain(other.Key);
        payload.ShouldNotContain("own-draft");
    }

    [Fact]
    public async Task A_reply_with_an_executable_attachment_is_400_attachment_type_not_allowed()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        using var form = new MultipartFormDataContent { { new StringContent("See attached"), "body" } };
        AddFile(form, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00], "setup.exe", "application/octet-stream");

        using var response = await sam.PostAsync($"/api/tickets/{seed.Tickets[0].Id}/replies", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("attachment-type-not-allowed");
        (await database.ScalarAsync<long>("SELECT count(*) FROM messages WHERE author_type = 'Agent'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_reply_to_a_closed_ticket_is_409()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var id = seed.Tickets[1].Id;
        await database.ExecuteAsync($"UPDATE tickets SET status = 'Closed', closed_at = now() WHERE id = '{id}'");
        using var form = new MultipartFormDataContent { { new StringContent("Too late"), "body" } };

        using var response = await sam.PostAsync($"/api/tickets/{id}/replies", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("ticket-closed");
    }
}
