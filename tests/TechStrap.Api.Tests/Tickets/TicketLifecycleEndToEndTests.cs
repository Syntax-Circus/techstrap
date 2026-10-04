using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Http;
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

    private static (string From, string To) Transition(TicketEventDto e)
    {
        using var json = System.Text.Json.JsonDocument.Parse(e.PayloadJson);
        return (json.RootElement.GetProperty("from").GetString()!, json.RootElement.GetProperty("to").GetString()!);
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
        var firstResponseAt = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{id}", Ct))!.FirstResponseAt;
        firstResponseAt.ShouldNotBeNull();

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

        final.FirstResponseAt.ShouldBe(firstResponseAt, "first response is stamped once");

        var types = final.Events.Select(e => e.Type).ToList();
        string[] expected =
        [
            nameof(TicketEventType.Created), nameof(TicketEventType.MessageAdded), nameof(TicketEventType.Assigned), nameof(TicketEventType.MessageAdded),
            nameof(TicketEventType.MessageAdded), nameof(TicketEventType.StatusChanged), nameof(TicketEventType.PriorityChanged),
            nameof(TicketEventType.TagAdded), nameof(TicketEventType.ProductChanged), nameof(TicketEventType.StatusChanged),
        ];
        types.ShouldBe(expected);
        var statusEvents = final.Events.Where(e => e.Type == nameof(TicketEventType.StatusChanged)).ToList();
        Transition(statusEvents[0]).ShouldBe(("New", "Pending")); // assigning does not open a New ticket
        Transition(statusEvents[1]).ShouldBe(("Pending", "Solved"));

        // 9. The agent downloads the PNG.
        using var download = await sam.GetAsync($"/api/attachments/{attachment.Id}", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
    }

    private static async Task<string> ScalarStringAsync(ApiTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static string TokenFromLink(string link)
    {
        const string marker = "/t/";
        return link[(link.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Split('?', '#', '/', '"')[0];
    }

    private static string TokenFromPortalLink(string payloadJson)
    {
        using var json = System.Text.Json.JsonDocument.Parse(payloadJson);
        return TokenFromLink(json.RootElement.GetProperty("portalLink").GetString()!);
    }

    private static async Task<CustomerTicketDto> CustomerViewAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CustomerTicketDto>(Ct))!;
    }

    private static async Task<HttpResponseMessage> CustomerReplyAsync(HttpClient client, string token, string body)
    {
        using var form = new MultipartFormDataContent { { new StringContent(body), "body" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/ticket/replies") { Content = form };
        request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>
    /// The auto-close handler is resolved from the Api test host's own container: the Api registers AutoCloseOptions but not the Worker-only
    /// handler, so the test adds the same scoped registration the Worker's AddTechStrapAutoClose makes, and replaces TimeProvider with a
    /// FakeTimeProvider (started at the real now) that the test advances past the configured days.
    /// </summary>
    [Fact]
    public async Task A_customer_follows_a_ticket_from_link_to_follow_up()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new ApiFactory(settings: settings, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.TryAddScoped<IAutoCloseSolvedTicketsHandler, AutoCloseSolvedTicketsHandler>();
        });
        await IntakeTestData.SeedAsync(factory.Services, Ct);
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var me = (await sam.GetFromJsonAsync<AgentDto>("/api/agents/me", Ct))!;
        using var anonymous = factory.CreateClient();

        // 1. Public form submission; the link token comes from the ticket-confirmation outbox payload.
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

        var token = TokenFromPortalLink(await ScalarStringAsync(database, "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation'"));

        // 2. The customer's first view: one message.
        var view = await CustomerViewAsync(anonymous, token);
        view.Number.ShouldBe("ORB-1");
        view.Messages.ShouldHaveSingleItem();

        // 3. Assign, then the agent replies in Markdown: the customer sees the public agent name.
        var detail = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        var id = detail.Id;
        using (var assigned = await sam.PutAsJsonAsync($"/api/tickets/{id}/assignee", new AssignTicketRequest(me.Id, detail.RowVersion), Ct))
        {
            assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var form = new MultipartFormDataContent { { new StringContent("Try **resetting**"), "body" } })
        using (var replied = await sam.PostAsync($"/api/tickets/{id}/replies", form, Ct))
        {
            replied.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        view = await CustomerViewAsync(anonymous, token);
        view.Status.ShouldBe("Pending");
        var agentMessage = view.Messages.Where(m => m.AuthorType == "Agent").ShouldHaveSingleItem();
        agentMessage.AuthorDisplayName.ShouldBe("Sam from Orbitly Support");
        agentMessage.BodyHtml.ShouldContain("<strong>resetting</strong>");

        // 4. The customer replies: the ticket reopens and the assignee is alerted.
        using (var customerReply = await CustomerReplyAsync(anonymous, token, "Still failing"))
        {
            customerReply.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await customerReply.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!.FollowUpCreated.ShouldBeFalse();
        }

        (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{id}", Ct))!.Status.ShouldBe("Open");
        (await ScalarStringAsync(database, "SELECT to_address FROM email_outbox WHERE kind = 'customer-reply-alert'")).ShouldBe("sam@example.com");

        // 5. Solve, move the clock past the auto-close days, run the handler: Closed, and no email.
        using (var solved = await sam.PutAsJsonAsync(
            $"/api/tickets/{id}/status", new ChangeTicketStatusRequest("Solved", await TicketTestData.VersionAsync(sam, id)), Ct))
        {
            solved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var outboxAfterSolve = (await OutboxKindsAsync(database)).Count;
        clock.Advance(TimeSpan.FromDays(8));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(Ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Closed.ShouldBe(1);
        }

        var closed = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{id}", Ct))!;
        closed.Status.ShouldBe("Closed");
        (await OutboxKindsAsync(database)).Count.ShouldBe(outboxAfterSolve, "auto-close sends no email");

        // 6. A reply on the Closed ticket creates a follow-up; the parent is untouched.
        string followUpToken;
        using (var followUp = await CustomerReplyAsync(anonymous, token, "It broke again"))
        {
            followUp.StatusCode.ShouldBe(HttpStatusCode.Created);
            var body = (await followUp.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!;
            body.FollowUpCreated.ShouldBeTrue();
            body.TicketNumber.ShouldNotBe("ORB-1");
            body.FollowUpViewUrl.ShouldNotBeNullOrWhiteSpace();
            followUpToken = TokenFromLink(body.FollowUpViewUrl!);
        }

        var followUpView = await CustomerViewAsync(anonymous, followUpToken);
        followUpView.Number.ShouldNotBe("ORB-1");
        followUpView.Messages.ShouldContain(m => m.BodyHtml.Contains("It broke again"));
        var parent = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{id}", Ct))!;
        parent.Status.ShouldBe("Closed");
        parent.Messages.Count.ShouldBe(closed.Messages.Count);
        parent.Events.Select(e => e.Type).ShouldContain(nameof(TicketEventType.FollowUpCreated));

        // 7. Lost link for the requester: 202 and one access-links row, to the requester only.
        using (var lost = await anonymous.PostAsJsonAsync("/api/customer/access-link", new RequestNewAccessLinkRequest("ada@example.com"), Ct))
        {
            lost.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'access-links'")).ShouldBe(1L);
        (await ScalarStringAsync(database, "SELECT to_address FROM email_outbox WHERE kind = 'access-links'")).ShouldBe("ada@example.com");
    }
}
