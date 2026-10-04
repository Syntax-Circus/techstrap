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
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.DeadLetters;
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

    private async Task<(ApiFactory Factory, ApiTestDatabase Database)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        await IntakeTestData.SeedAsync(factory.Services, Ct);
        return (factory, database);
    }

    /// <summary>A client in the admin group; GET /api/agents/me provisions its agent row.</summary>
    private static async Task<HttpClient> AdminClientAsync(ApiFactory factory)
    {
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com", name: "Ada Admin"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return admin;
    }

    private static async Task SubmitAsync(HttpClient anonymous, string body, bool withPng = false)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" }, { new StringContent("Ada"), "name" },
            { new StringContent("Cannot log in"), "subject" }, { new StringContent(body), "body" },
        };
        if (withPng)
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(file, "attachments", "shot.png");
        }

        using var submitted = await anonymous.PostAsync("/api/public/products/orbitly/tickets", form, Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_admin_deletes_a_closed_ticket_and_its_follow_up_survives_unlinked()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        using var anonymous = factory.CreateClient();

        await SubmitAsync(anonymous, "Please help");
        var token = TokenFromPortalLink(await ScalarStringAsync(database, "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation'"));
        var parent = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        foreach (var status in new[] { "Solved", "Closed" })
        {
            using var changed = await sam.PutAsJsonAsync(
                $"/api/tickets/{parent.Id}/status", new ChangeTicketStatusRequest(status, await TicketTestData.VersionAsync(sam, parent.Id)), Ct);
            changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        CustomerReplyResponse followUp;
        using (var reply = await CustomerReplyAsync(anonymous, token, "It broke again"))
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.Created);
            followUp = (await reply.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!;
        }

        var followUpToken = TokenFromLink(followUp.FollowUpViewUrl!);

        // An agent may not delete; an admin may.
        using (var refused = await sam.DeleteAsync($"/api/tickets/{parent.Id}", Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        using (var deleted = await admin.DeleteAsync($"/api/tickets/{parent.Id}", Ct)) { deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent); }

        foreach (var table in new[] { "messages", "ticket_events", "ticket_access_tokens", "attachments", "ticket_tags", "email_outbox" })
        {
            (await database.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE ticket_id = '{parent.Id}'")).ShouldBe(0L, table);
        }

        (await database.ScalarAsync<long>($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0L);
        (await database.ScalarAsync<bool>($"SELECT parent_ticket_id IS NULL FROM tickets WHERE number = '{followUp.TicketNumber}'")).ShouldBeTrue();
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM ticket_events e JOIN tickets t ON t.id = e.ticket_id WHERE t.number = '{followUp.TicketNumber}' AND e.payload::text LIKE '%parentTicketId%'"))
            .ShouldBe(1L, "the follow-up's own Created event keeps its history");
        using (var gone = await sam.GetAsync($"/api/tickets/{parent.Id}", Ct)) { gone.StatusCode.ShouldBe(HttpStatusCode.NotFound); }
        (await CustomerViewAsync(anonymous, followUpToken)).Number.ShouldBe(followUp.TicketNumber);
        using (var stale = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket"))
        {
            stale.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            (await anonymous.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Ticket", Ct))!;
        var entry = audit.Items.Single(e => e.Type == "TicketDeleted" && e.SubjectId == parent.Id);
        entry.Payload.ShouldNotContain("Cannot log in");
        entry.Payload.ShouldNotContain("ada@example.com");
    }

    [Fact]
    public async Task An_admin_erases_a_requester_and_the_customer_view_and_agent_queue_show_no_trace()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        using var anonymous = factory.CreateClient();

        await SubmitAsync(anonymous, "zebracanary reach me at ada@example.com", withPng: true);
        var token = TokenFromPortalLink(await ScalarStringAsync(database, "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation'"));
        var before = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        using (var form = new MultipartFormDataContent { { new StringContent("Agent reply stays"), "body" } })
        using (var replied = await sam.PostAsync($"/api/tickets/{before.Id}/replies", form, Ct)) { replied.StatusCode.ShouldBe(HttpStatusCode.Created); }
        before = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=All&search=zebracanary", Ct))!.Items.ShouldHaveSingleItem();
        Directory.EnumerateFiles(_storage, "*", SearchOption.AllDirectories).ShouldNotBeEmpty();
        var requesterId = await database.ScalarAsync<Guid>("SELECT id FROM requesters WHERE email = 'ada@example.com'");

        using (var refused = await sam.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        using (var erased = await admin.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { erased.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        using (var again = await admin.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { again.StatusCode.ShouldBe(HttpStatusCode.NoContent); }

        var after = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        after.Number.ShouldBe("ORB-1");
        after.Subject.ShouldBe("[erased]");
        after.Events.Count.ShouldBe(before.Events.Count);
        after.Messages.Where(m => m.AuthorType == "Requester").ShouldAllBe(m => m.BodyHtml == "[erased]");
        after.Messages.Single(m => m.AuthorType == "Agent").BodyHtml.ShouldContain("Agent reply stays");
        (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=All&search=zebracanary", Ct))!.Items.ShouldBeEmpty();
        Directory.EnumerateFiles(_storage, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        using (var stale = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket"))
        {
            stale.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            (await anonymous.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        foreach (var sql in new[]
        {
            "SELECT count(*) FROM requesters WHERE email = 'ada@example.com' OR name = 'Ada'",
            "SELECT count(*) FROM email_outbox WHERE to_address = 'ada@example.com' OR ticket_id IS NOT NULL",
            "SELECT count(*) FROM attachments",
            "SELECT count(*) FROM messages WHERE body LIKE '%ada@example.com%' OR body LIKE '%zebracanary%'",
            "SELECT count(*) FROM tickets WHERE subject <> '[erased]'",
        })
        {
            (await database.ScalarAsync<long>(sql)).ShouldBe(0L, sql);
        }

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Requester", Ct))!;
        audit.Items.ShouldAllBe(e => e.Type == "RequesterErased" && e.SubjectId == requesterId);
        audit.Items.ShouldAllBe(e => !e.Payload.Contains("ada@example.com") && !e.Payload.Contains("Ada"));
    }

    [Fact]
    public async Task An_admin_lists_retries_and_discards_dead_letters_and_an_agent_cannot()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        var retryId = Guid.NewGuid();
        var discardId = Guid.NewGuid();
        foreach (var id in new[] { retryId, discardId })
        {
            await database.ExecuteAsync($$"""
                INSERT INTO email_outbox (id, kind, to_address, payload, status, attempts, next_attempt_at, last_error, created_at)
                VALUES ('{{id}}', 'ticket-confirmation', 'ada@example.com', '{"portalLink":"https://help.test/t/secret"}', 'DeadLettered', 5, now(), 'render-failed', now())
                """);
        }

        using (var refused = await sam.GetAsync("/api/dead-letters", Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        var raw = await admin.GetStringAsync("/api/dead-letters", Ct);
        raw.ShouldNotContain("ada@example.com");
        raw.ShouldNotContain("secret");
        raw.ShouldNotContain("payload", Case.Insensitive);
        var list = System.Text.Json.JsonSerializer.Deserialize<PagedResponse<DeadLetterDto>>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
        list.TotalCount.ShouldBe(2);
        list.Items.ShouldAllBe(d => d.Recipient == "a***@example.com" && d.Kind == "ticket-confirmation" && d.Attempts == 5 && d.LastError == "render-failed");

        using (var retried = await admin.PostAsync($"/api/dead-letters/{retryId}/retry", null, Ct)) { retried.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        (await ScalarStringAsync(database, $"SELECT status FROM email_outbox WHERE id = '{retryId}'")).ShouldBe("Pending");
        (await database.ScalarAsync<int>($"SELECT attempts FROM email_outbox WHERE id = '{retryId}'")).ShouldBe(0);
        using (var twice = await admin.PostAsync($"/api/dead-letters/{retryId}/retry", null, Ct)) { twice.StatusCode.ShouldBe(HttpStatusCode.Conflict); }
        using (var discarded = await admin.DeleteAsync($"/api/dead-letters/{discardId}", Ct)) { discarded.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        using (var missing = await admin.DeleteAsync($"/api/dead-letters/{Guid.NewGuid()}", Ct)) { missing.StatusCode.ShouldBe(HttpStatusCode.NotFound); }
        (await admin.GetFromJsonAsync<PagedResponse<DeadLetterDto>>("/api/dead-letters", Ct))!.Items.ShouldBeEmpty();

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=EmailOutbox", Ct))!;
        audit.Items.Select(e => e.Type).ShouldBe(["DeadLetterRetried", "DeadLetterDiscarded"], ignoreOrder: true);
    }
}
