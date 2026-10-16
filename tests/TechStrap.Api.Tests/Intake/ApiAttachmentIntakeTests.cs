using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Api.Tests.Intake;

public sealed class ApiAttachmentIntakeTests(TestPostgres postgres) : IAsyncLifetime
{
    private const string Route = "/api/intake/tickets/with-attachments";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-sdk-files-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storage)) Directory.Delete(_storage, true);
        return ValueTask.CompletedTask;
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync(bool kestrel = false, Action<IServiceCollection>? configure = null, int? permitLimit = null)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
            ["RateLimiting:Intake:TrustedKeyPermitLimit"] = (permitLimit ?? 120).ToString(),
        }, configureServices: configure);
        if (kestrel) factory.UseKestrel(0);
        var seed = await IntakeTestData.SeedAsync(factory.Services, Ct);
        return (factory, database, seed);
    }

    private static MultipartFormDataContent Form(string metadata = "{\"plan\":\"pro\"}", string type = "Support", int files = 1)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" },
            { new StringContent("Ada"), "name" },
            { new StringContent("Help"), "subject" },
            { new StringContent("Please help"), "body" },
            { new StringContent("user-1"), "externalUserRef" },
            { new StringContent(metadata), "metadata" },
            { new StringContent(type), "type" },
        };
        for (var i = 0; i < files; i++)
        {
            var file = new ByteArrayContent("hello from the SDK"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "attachments", $"trace-{i}.log");
        }
        return form;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string? key, HttpContent form, string? idempotencyKey = null, bool expectContinue = false)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Route) { Content = form };
        message.Headers.ExpectContinue = expectContinue;
        if (key is not null) message.Headers.Add(HeaderNames.ApiKey, key);
        if (idempotencyKey is not null) message.Headers.Add(HeaderNames.IdempotencyKey, idempotencyKey);
        return client.SendAsync(message, Ct);
    }

    private static async Task EnableWebhookAsync(ApiFactory factory)
    {
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        using var me = await admin.GetAsync("/api/agents/me", Ct);
        me.EnsureSuccessStatusCode();
        using var settings = await admin.PutAsJsonAsync("/api/settings/webhooks", new UpdateWebhookSettingsRequest("Replace", 0,
            [new() { Url = "https://discord.com/api/webhooks/123/secret" }]), Ct);
        settings.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(true, "Support")]
    [InlineData(false, "FeatureRequest")]
    public async Task Keyed_multipart_preserves_trust_type_and_stores_files(bool trusted, string type)
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, trusted ? seed.OrbitlyTrusted : seed.OrbitlyPublic, Form(type: type));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(Ct))!;
        body.ViewUrl.ShouldStartWith("https://help.test/t/");
        body.Warnings.Contains(IntakeWarnings.ExternalUserRefIgnored).ShouldBe(!trusted);
        (await database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets")).ShouldBe(trusted);
        (await database.ScalarAsync<string>("SELECT type FROM tickets")).ShouldBe(type);
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(1);
        var stored = Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(stored, Ct)).ShouldBe("hello from the SDK");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"plan\":42}")]
    public async Task Invalid_metadata_is_a_safe_field_error_without_writes(string metadata)
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, seed.OrbitlyTrusted, Form(metadata));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("metadata-invalid");
        body.ShouldContain("metadata");
        body.ShouldNotContain("JsonException");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Fact]
    public async Task Authentication_precedes_multipart_validation()
    {
        var (factory, _, _) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, null, new StringContent("{}"));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Too_many_files_leave_no_ticket_or_files()
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, seed.OrbitlyTrusted, Form(files: 6));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("attachments-too-many");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
        if (Directory.Exists(_storage)) Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_same_key_uploads_replay_the_winner_and_remove_the_losing_files()
    {
        var gate = new LookupGate();
        var (factory, database, seed) = await StartAsync(configure: services =>
        {
            var real = services.Last(d => d.ServiceType == typeof(IIntakeIdempotencyStore));
            services.Remove(real);
            services.AddScoped<IIntakeIdempotencyStore>(provider => new GatedIdempotencyStore(
                (IIntakeIdempotencyStore)(real.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)), gate));
        });
        await using var lifetime = factory;
        await EnableWebhookAsync(factory);
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(PostAsync(client, seed.OrbitlyTrusted, Form(), "race"), PostAsync(client, seed.OrbitlyTrusted, Form(), "race"));
        foreach (var response in responses)
        {
            using (response)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
                (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(Ct))!.TicketNumber.ShouldBe("ORB-1");
            }
        }
        gate.Arrivals.ShouldBeGreaterThanOrEqualTo(2);
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation'")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries")).ShouldBe(1);
        Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
    }

    private sealed class LookupGate
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Arrivals;
        public async Task WaitAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref Arrivals) == 2) _both.TrySetResult();
            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
    }

    private sealed class GatedIdempotencyStore(IIntakeIdempotencyStore inner, LookupGate gate) : IIntakeIdempotencyStore
    {
        public async Task<IntakeIdempotencyEntry?> FindAsync(Guid apiKeyId, string key, CancellationToken ct)
        {
            var entry = await inner.FindAsync(apiKeyId, key, ct);
            await gate.WaitAsync(ct);
            return entry;
        }
        public void Add(Guid apiKeyId, string key, Guid ticketId, string json, DateTimeOffset now) => inner.Add(apiKeyId, key, ticketId, json, now);
        public void Remove(IntakeIdempotencyEntry entry) => inner.Remove(entry);
        public Task<int> PruneAsync(DateTimeOffset olderThan, int limit, CancellationToken ct) => inner.PruneAsync(olderThan, limit, ct);
    }

    [Fact]
    public async Task Bad_file_after_a_good_file_rolls_back_and_cleans_storage()
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var form = Form();
        var invalid = new ByteArrayContent("not PNG bytes"u8.ToArray());
        invalid.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(invalid, "attachments", "fake.png");
        using var response = await PostAsync(client, seed.OrbitlyTrusted, form);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("attachment-type-not-allowed");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(0);
        Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("application/json", "{}", 415)]
    [InlineData("multipart/form-data; boundary=abc", "not a form", 400)]
    public async Task Wrong_or_malformed_multipart_is_a_safe_problem(string contentType, string body, int status)
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        var content = new StringContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await PostAsync(client, seed.OrbitlyTrusted, content);
        ((int)response.StatusCode).ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Fact]
    public async Task Oversized_multipart_body_is_413_on_a_real_socket()
    {
        var (factory, _, seed) = await StartAsync(kestrel: true);
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var form = Form(files: 0);
        form.Add(new ByteArrayContent(new byte[IntakeLimits.FormBodyBytes]), "attachments", "large.log");
        using var response = await PostAsync(client, seed.OrbitlyTrusted, form, expectContinue: true);
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("request-too-large");
    }

    [Fact]
    public async Task Json_and_multipart_share_the_same_key_rate_limit()
    {
        var (factory, _, seed) = await StartAsync(permitLimit: 1);
        await using var lifetime = factory;
        using var client = factory.CreateClient();
        using var first = await PostAsync(client, seed.OrbitlyTrusted, Form());
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
        {
            Content = JsonContent.Create(new SubmitTicketRequest("ada@example.com", null, "Help", "Help", null, null)),
        };
        json.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
        using var second = await client.SendAsync(json, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Replay_across_formats_does_not_add_files_or_notifications()
    {
        var (factory, database, seed) = await StartAsync();
        await using var lifetime = factory;
        await EnableWebhookAsync(factory);
        using var client = factory.CreateClient();
        using var first = await PostAsync(client, seed.OrbitlyTrusted, Form(), "same-ticket");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var notifications = await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox");
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
        {
            Content = JsonContent.Create(new SubmitTicketRequest("ada@example.com", null, "Other", "Other", null, null)),
        };
        replay.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
        replay.Headers.Add(HeaderNames.IdempotencyKey, "same-ticket");
        using var second = await client.SendAsync(replay, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var third = await PostAsync(client, seed.OrbitlyTrusted, Form(files: 2), "same-ticket");
        third.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox")).ShouldBe(notifications);
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries")).ShouldBe(1);
        Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
    }
}
