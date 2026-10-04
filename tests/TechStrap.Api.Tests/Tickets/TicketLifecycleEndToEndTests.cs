using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Tickets;

namespace TechStrap.Api.Tests.Tickets;

/// <summary>
/// Submission to Solved through the real public intake and the real agent API. Mailpit delivery is covered by the Infrastructure
/// EmailDrainIntegrationTests (Api.Tests has no reference to that fixture), so email is asserted on email_outbox rows here.
/// </summary>
public sealed class TicketLifecycleEndToEndTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-lifecycle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private static async Task<IReadOnlyList<string>> OutboxKindsAsync(ApiTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT kind FROM email_outbox", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var kinds = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            kinds.Add(reader.GetString(0));
        }

        return kinds;
    }

    [Fact]
    public async Task A_ticket_goes_from_submission_to_solved_through_the_agent_api()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        await using var factory = new ApiFactory(settings: settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, Ct);
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var me = (await sam.GetFromJsonAsync<AgentDto>("/api/agents/me", Ct))!;

        Guid tagId, articleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var clock = provider.GetRequiredService<TimeProvider>();
            var tag = Tag.Create("billing", "Billing", "#DC2626", clock).Value;
            var article = KbArticle.Create(null, null, "reset-password", "Reset your password", null, "Steps.", me.Id, clock).Value;
            article.Publish(clock).IsSuccess.ShouldBeTrue();
            tagId = tag.Id;
            articleId = article.Id;
            await using var work = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            provider.GetRequiredService<ITagRepository>().Add(tag);
            provider.GetRequiredService<IKbRepository>().AddArticle(article);
            (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }

        // 1. Public form submission.
        using (var anonymous = factory.CreateClient())
        using (var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" },
            { new StringContent("Ada"), "name" },
            { new StringContent("Cannot log in"), "subject" },
            { new StringContent("Please help"), "body" },
        })
        using (var submitted = await anonymous.PostAsync("/api/public/products/orbitly/tickets", form, Ct))
        {
            submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // 2. It sits in the Unassigned queue.
        var unassigned = (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=Unassigned", Ct))!;
        var row = unassigned.Items.ShouldHaveSingleItem();
        row.Number.ShouldBe("ORB-1");
        (await sam.GetFromJsonAsync<TicketViewCountsResponse>("/api/tickets/counts", Ct))!.Unassigned.ShouldBeGreaterThanOrEqualTo(1);
        var id = row.Id;

        // 3. Self-assign: no ticket-assigned email.
        var detail = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        using (var assigned = await sam.PutAsJsonAsync($"/api/tickets/{id}/assignee", new AssignTicketRequest(me.Id, detail.RowVersion), Ct))
        {
            assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await OutboxKindsAsync(database)).ShouldNotContain("ticket-assigned");

        // 4. Internal note.
        using (var noted = await sam.PostAsJsonAsync($"/api/tickets/{id}/notes", new AddInternalNoteRequest("Checked the **logs**", null), Ct))
        {
            noted.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var afterNote = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{id}", Ct))!;
        afterNote.Messages.ShouldContain(m => m.Visibility == "Internal" && m.BodyHtml.Contains("<strong>logs</strong>"));

        // 5. Public reply with a PNG and a linked article.
        AttachmentDto attachment;
        using (var reply = new MultipartFormDataContent
        {
            { new StringContent("Hi **Ada**, try resetting"), "body" },
            { new StringContent(articleId.ToString()), "linkedArticleIds" },
        })
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            reply.Add(file, "attachments", "pixel.png");
            using var replied = await sam.PostAsync($"/api/tickets/{id}/replies", reply, Ct);
            replied.StatusCode.ShouldBe(HttpStatusCode.Created);
            var body = (await replied.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct))!;
            body.Ticket.Status.ShouldBe("Pending");
            body.Message.LinkedArticles.ShouldHaveSingleItem().Id.ShouldBe(articleId);
            attachment = body.Message.Attachments.ShouldHaveSingleItem();
        }

        (await OutboxKindsAsync(database)).ShouldBe(["ticket-confirmation", "agent-reply"], ignoreOrder: true);

        // 6. Priority, tag, product move: the number stays.
        var version = await TicketTestData.VersionAsync(sam, id);
        using (var prioritised = await sam.PutAsJsonAsync($"/api/tickets/{id}/priority", new ChangeTicketPriorityRequest("High", version), Ct))
        {
            prioritised.StatusCode.ShouldBe(HttpStatusCode.OK);
            version = (await prioritised.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!.RowVersion;
        }

        using (var tagged = await sam.PostAsJsonAsync($"/api/tickets/{id}/tags", new AddTicketTagRequest(tagId, version), Ct))
        {
            tagged.StatusCode.ShouldBe(HttpStatusCode.OK);
            version = (await tagged.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!.RowVersion;
        }

        using (var moved = await sam.PutAsJsonAsync($"/api/tickets/{id}/product", new MoveTicketProductRequest(seed.Paperplane.Id, version), Ct))
        {
            moved.StatusCode.ShouldBe(HttpStatusCode.OK);
            version = (await moved.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!.RowVersion;
        }

        // 7. Solve.
        using (var solved = await sam.PutAsJsonAsync($"/api/tickets/{id}/status", new ChangeTicketStatusRequest("Solved", version), Ct))
        {
            solved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await OutboxKindsAsync(database)).ShouldBe(["ticket-confirmation", "agent-reply", "ticket-solved"], ignoreOrder: true);

        // 8. The timeline.
        var final = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        final.Number.ShouldBe("ORB-1");
        final.Status.ShouldBe("Solved");
        final.Priority.ShouldBe("High");
        final.ProductId.ShouldBe(seed.Paperplane.Id);
        final.Tags.ShouldHaveSingleItem().Id.ShouldBe(tagId);
        final.SolvedAt.ShouldNotBeNull();
        final.FirstResponseAt.ShouldNotBeNull();
        final.Messages.Count(m => m.Visibility == "Internal").ShouldBe(1);
        final.Messages.Count(m => m.AuthorType == "Agent" && m.Visibility == "Public").ShouldBe(1);

        var types = final.Events.Select(e => e.Type).ToList();
        string[] expected =
        [
            nameof(TicketEventType.Created), nameof(TicketEventType.Assigned), nameof(TicketEventType.MessageAdded),
            nameof(TicketEventType.MessageAdded), nameof(TicketEventType.StatusChanged), nameof(TicketEventType.PriorityChanged),
            nameof(TicketEventType.TagAdded), nameof(TicketEventType.ProductChanged), nameof(TicketEventType.StatusChanged),
        ];
        var cursor = 0;
        foreach (var type in types)
        {
            if (cursor < expected.Length && type == expected[cursor])
            {
                cursor++;
            }
        }

        cursor.ShouldBe(expected.Length, $"events in order should contain {string.Join(", ", expected)} but were {string.Join(", ", types)}");
        final.Events[^1].Type.ShouldBe(nameof(TicketEventType.StatusChanged));
        final.Events[^1].PayloadJson.ShouldContain("Solved");

        // 9. The agent downloads the PNG.
        using var download = await sam.GetAsync($"/api/attachments/{attachment.Id}", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
    }
}
