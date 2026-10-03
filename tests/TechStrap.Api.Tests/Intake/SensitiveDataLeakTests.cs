using System.Net;
using System.Net.Http.Json;
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
        var columns = new List<(string Table, string Column)>();
        await using (var catalogue = new NpgsqlCommand(
            "SELECT c.table_name, c.column_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema = 'public' AND (c.data_type IN ('text', 'character varying', 'jsonb', 'json', 'ARRAY') OR c.udt_name = 'citext')",
            connection))
        await using (var reader = await catalogue.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

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
}
