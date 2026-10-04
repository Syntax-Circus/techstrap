using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class SensitiveDataLeakTests(TestPostgres postgres) : IAsyncLifetime
{
    private const string TokenMarker = "/t/";

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-leak-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        return ValueTask.CompletedTask;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string key, SubmitTicketRequest body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = JsonContent.Create(body) };
        request.Headers.Add(HeaderNames.ApiKey, key);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(HeaderNames.IdempotencyKey, idempotencyKey);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, database, seed);
    }

    private static string TokenOf(string viewUrl)
    {
        var index = viewUrl.IndexOf(TokenMarker, StringComparison.Ordinal);
        index.ShouldBeGreaterThan(-1);
        return viewUrl[(index + TokenMarker.Length)..].Split('?', '#', '/')[0];
    }

    private static async Task<List<(string Table, string Column)>> TextColumnsAsync(NpgsqlConnection connection)
    {
        var columns = new List<(string Table, string Column)>();
        await using var catalogue = new NpgsqlCommand(
            "SELECT c.table_name, c.column_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema = 'public' AND (c.data_type IN ('text', 'character varying', 'jsonb', 'json', 'ARRAY') OR c.udt_name = 'citext')",
            connection);
        await using var reader = await catalogue.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        return columns;
    }

    [Fact]
    public async Task A_key_submission_leaves_the_plaintext_key_and_token_out_of_logs_and_every_column_but_the_outbox_payload()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", null, null);
        var key = seed.OrbitlyTrusted;
        var idempotencyKey = Guid.NewGuid().ToString();

        using var first = await PostAsync(client, key, request, idempotencyKey);
        using var replay = await PostAsync(client, key, request, idempotencyKey);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = (await first.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        var replayBody = (await replay.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        var firstToken = TokenOf(firstBody.ViewUrl!);
        var replayToken = TokenOf(replayBody.ViewUrl!);
        replayToken.ShouldNotBe(firstToken);

        // 1. Logs
        var needles = new[] { key, firstToken, replayToken };
        factory.LogSink.Events.ShouldNotBeEmpty();
        foreach (var needle in needles)
        {
            factory.LogSink.Events.ShouldAllBe(e => !e.RenderMessage().Contains(needle)
                && !e.Properties.Values.Any(v => v.ToString().Contains(needle)));
        }

        // 2. Every textual column at rest
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var columns = await TextColumnsAsync(connection);

        columns.ShouldContain(("email_outbox", "payload"));

        // The text cast cannot scan bytea, so the schema must not hold any.
        await using (var binary = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND data_type = 'bytea'", connection))
        {
            ((long)(await binary.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBe(0L, "bytea columns would escape the leak scan");
        }
        foreach (var (table, column) in columns)
        {
            foreach (var needle in needles)
            {
                var sql = $"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"::text LIKE '%' || @needle || '%'";
                await using var count = new NpgsqlCommand(sql, connection);
                count.Parameters.AddWithValue("needle", needle);
                var found = (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
                var holdsFirstToken = table == "email_outbox" && column == "payload" && needle == firstToken;
                found.ShouldBe(holdsFirstToken ? 1L : 0L, $"{table}.{column}");
            }
        }

        // 3. Token hashes
        var hashValues = new List<string>();
        await using (var hashes = new NpgsqlCommand("SELECT token_hash FROM ticket_access_tokens", connection))
        await using (var reader = await hashes.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                hashValues.Add(reader.GetString(0));
            }
        }

        hashValues.ShouldNotBeEmpty();
        hashValues.ShouldAllBe(h => h.StartsWith("sha256:", StringComparison.Ordinal) && h != firstToken && h != replayToken);
    }

    [Fact]
    public async Task The_customer_view_and_customer_emails_carry_no_internal_or_agent_private_data()
    {
        const string internalCanary = "INTERNAL-CANARY";
        const string tagCanary = "TAG-CANARY";
        const string agentEmail = "sam.private@example.com";
        const string ada = "ada@example.com";
        const string bob = "bob.other@example.com";
        var ct = TestContext.Current.CancellationToken;
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        using var anonymous = factory.CreateClient();

        async Task SubmitAsync(string email, string name, string subject)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(email), "email" }, { new StringContent(name), "name" },
                { new StringContent(subject), "subject" }, { new StringContent("Please help"), "body" },
            };
            using var response = await anonymous.PostAsync("/api/public/products/orbitly/tickets", form, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await SubmitAsync(ada, "Ada", "Cannot log in");
        await SubmitAsync(bob, "Bob", "Other problem");

        Guid tagId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tag = TechStrap.Domain.Tickets.Tag.Create("canary", tagCanary, "#DC2626", scope.ServiceProvider.GetRequiredService<TimeProvider>()).Value;
            tagId = tag.Id;
            await using var work = await scope.ServiceProvider.GetRequiredService<TechStrap.Application.Persistence.IUnitOfWork>().BeginAsync(ct);
            scope.ServiceProvider.GetRequiredService<TechStrap.Application.Persistence.ITagRepository>().Add(tag);
            (await work.CommitAsync(ct)).IsSuccess.ShouldBeTrue();
        }

        using var sam = factory.CreateClient().Bearer(TestJwt.Token("sam", [TestJwt.AgentGroup], email: agentEmail, name: "Sam Hargreaves"));
        (await sam.GetAsync("/api/agents/me", ct)).EnsureSuccessStatusCode();
        var detail = (await sam.GetFromJsonAsync<TechStrap.Contracts.Tickets.TicketDetailDto>("/api/tickets/ORB-1", ct))!;
        var id = detail.Id;

        using (var noted = await sam.PostAsJsonAsync($"/api/tickets/{id}/notes", new TechStrap.Contracts.Tickets.AddInternalNoteRequest(internalCanary, null), ct))
        {
            noted.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var tagged = await sam.PostAsJsonAsync(
            $"/api/tickets/{id}/tags", new TechStrap.Contracts.Tickets.AddTicketTagRequest(tagId, await Tickets.TicketTestData.VersionAsync(sam, id)), ct))
        {
            tagged.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var form = new MultipartFormDataContent { { new StringContent("We are looking into it"), "body" } })
        using (var replied = await sam.PostAsync($"/api/tickets/{id}/replies", form, ct))
        {
            replied.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        foreach (var status in new[] { "Solved", "Closed" })
        {
            using var changed = await sam.PutAsJsonAsync(
                $"/api/tickets/{id}/status", new TechStrap.Contracts.Tickets.ChangeTicketStatusRequest(status, await Tickets.TicketTestData.VersionAsync(sam, id)), ct);
            changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var firstPayload = await database.ScalarAsync<string>(
            "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation' AND to_address = 'ada@example.com'");
        using var firstJson = System.Text.Json.JsonDocument.Parse(firstPayload);
        var token = TokenOf(firstJson.RootElement.GetProperty("portalLink").GetString()!);

        async Task<string> ViewAsync(string viewToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
            request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, viewToken);
            using var response = await anonymous.SendAsync(request, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync(ct);
        }

        // The reply on the Closed ticket creates the follow-up.
        string followUpToken;
        using (var form = new MultipartFormDataContent { { new StringContent("It broke again"), "body" } })
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/ticket/replies") { Content = form })
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            using var followUp = await anonymous.SendAsync(request, ct);
            followUp.StatusCode.ShouldBe(HttpStatusCode.Created);
            var body = (await followUp.Content.ReadFromJsonAsync<TechStrap.Contracts.Tickets.CustomerReplyResponse>(ct))!;
            body.FollowUpCreated.ShouldBeTrue();
            followUpToken = TokenOf(body.FollowUpViewUrl!);
        }

        using (var lost = await anonymous.PostAsJsonAsync("/api/customer/access-link", new TechStrap.Contracts.Tickets.RequestNewAccessLinkRequest(ada), ct))
        {
            lost.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        // 1. The serialized customer views (parent and follow-up) carry none of the private data.
        string[] forbidden = [internalCanary, "Hargreaves", agentEmail, tagCanary, bob, "lastActivityAt", "rowVersion"];
        var parentView = await ViewAsync(token);
        parentView.ShouldContain("Sam from Orbitly Support");
        foreach (var json in new[] { parentView, await ViewAsync(followUpToken) })
        {
            foreach (var needle in forbidden)
            {
                json.ShouldNotContain(needle, Case.Insensitive);
            }
        }

        // 2. Every customer-facing email goes to the requester's own address, and its payload carries no private data.
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'access-links'")).ShouldBe(1L);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'access-links' AND to_address <> 'ada@example.com'")).ShouldBe(0L);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation' AND to_address = 'ada@example.com'"))
            .ShouldBe(2L, "the original and the follow-up confirmation both go to the requester");
        (await database.ScalarAsync<long>(
            "SELECT count(*) FROM email_outbox WHERE kind IN ('ticket-confirmation', 'agent-reply', 'ticket-solved', 'access-links') " +
            "AND to_address NOT IN ('ada@example.com', 'bob.other@example.com')")).ShouldBe(0L);

        // Bob receives exactly his own intake confirmation and nothing else.
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE to_address = 'bob.other@example.com'")).ShouldBe(1L);
        (await database.ScalarAsync<long>(
            "SELECT count(*) FROM email_outbox WHERE to_address = 'bob.other@example.com' AND kind = 'ticket-confirmation' AND payload::text LIKE '%ORB-2%'"))
            .ShouldBe(1L, "the other requester receives only his own intake confirmation");

        var payloads = new List<(string Kind, string Payload)>();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(ct);
        await using (var rows = new NpgsqlCommand(
            "SELECT kind, payload::text FROM email_outbox WHERE to_address = 'ada@example.com' " +
            "AND kind IN ('ticket-confirmation', 'agent-reply', 'ticket-solved', 'access-links')", connection))
        await using (var reader = await rows.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                payloads.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        payloads.ShouldNotBeEmpty();
        payloads.ShouldContain(p => p.Kind == "agent-reply" && p.Payload.Contains("Sam from Orbitly Support"));
        foreach (var (kind, payload) in payloads)
        {
            foreach (var needle in forbidden)
            {
                payload.ShouldNotContain(needle, Case.Insensitive, $"{kind} payload");
            }
        }

        var followUpConfirmation = payloads.Where(p => p.Kind == "ticket-confirmation" && !p.Payload.Contains(token)).ShouldHaveSingleItem();
        using var followUpJson = System.Text.Json.JsonDocument.Parse(followUpConfirmation.Payload);
        TokenOf(followUpJson.RootElement.GetProperty("portalLink").GetString()!).ShouldNotBe(followUpToken);

        // 3. The original link token is in exactly one outbox payload and no other column; the follow-up response token is stored nowhere.
        foreach (var (table, column) in await TextColumnsAsync(connection))
        {
            foreach (var needle in new[] { token, followUpToken })
            {
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"::text LIKE '%' || @needle || '%'", connection);
                count.Parameters.AddWithValue("needle", needle);
                var found = (long)(await count.ExecuteScalarAsync(ct))!;
                var expected = needle == token && table == "email_outbox" && column == "payload" ? 1L : 0L;
                found.ShouldBe(expected, needle == token
                    ? $"{table}.{column} for the original link token"
                    : $"{table}.{column}: the follow-up response token is never stored, the confirmation email carries its own token");
            }
        }
    }

    [Fact]
    public async Task Error_responses_never_echo_the_api_key()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("not-an-email", "Ada", "Help", "Please help", null, null);

        using var response = await PostAsync(client, seed.OrbitlyTrusted, request, null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(seed.OrbitlyTrusted);
    }

    [Fact]
    public async Task An_agent_reply_keeps_the_agent_email_out_of_the_outbox_and_the_link_only_in_the_payload()
    {
        const string agentEmail = "sam.agent@example.com";
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var created = await PostAsync(client, seed.OrbitlyTrusted, new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", null, null), null);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var number = (await created.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!.TicketNumber;

        using var sam = factory.CreateClient().Bearer(TestJwt.Token("sam", [TestJwt.AgentGroup], email: agentEmail, name: "Sam Hargreaves"));
        (await sam.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var detail = (await sam.GetFromJsonAsync<TechStrap.Contracts.Tickets.TicketDetailDto>($"/api/tickets/{number}", TestContext.Current.CancellationToken))!;
        using var form = new MultipartFormDataContent { { new StringContent("We are looking into it"), "body" } };
        using var replied = await sam.PostAsync($"/api/tickets/{detail.Id}/replies", form, TestContext.Current.CancellationToken);
        replied.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        string payload;
        await using (var command = new NpgsqlCommand("SELECT payload::text FROM email_outbox WHERE kind = 'agent-reply'", connection))
        {
            payload = (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        payload.ShouldContain("Sam from Orbitly Support");
        payload.ShouldNotContain("Hargreaves");
        payload.ShouldNotContain("@");
        var markerAt = payload.IndexOf(TokenMarker, StringComparison.Ordinal);
        markerAt.ShouldBeGreaterThan(-1);
        var token = payload[(markerAt + TokenMarker.Length)..].Split('?', '#', '/', '"', '\\')[0];
        token.ShouldNotBeNullOrWhiteSpace();

        var columns = await TextColumnsAsync(connection);
        columns.ShouldContain(("email_outbox", "payload"));
        foreach (var (table, column) in columns)
        {
            foreach (var needle in new[] { token, agentEmail, "Hargreaves" })
            {
                var sql = $"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"::text LIKE '%' || @needle || '%'";
                await using var count = new NpgsqlCommand(sql, connection);
                count.Parameters.AddWithValue("needle", needle);
                var found = (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
                var expected = needle == token && table == "email_outbox" && column == "payload" ? 1L : 0L;
                if ((needle == agentEmail && table == "agents" && column == "email") || (needle == "Hargreaves" && table == "agents" && column == "name"))
                {
                    continue; // the agent's own profile row legitimately holds it
                }

                found.ShouldBe(expected, $"{table}.{column} for {(needle == token ? "the reply token" : "the agent email or surname")}");
            }
        }

        factory.LogSink.Events.ShouldAllBe(e => !e.RenderMessage().Contains(token) && !e.RenderMessage().Contains(agentEmail));
    }
}
