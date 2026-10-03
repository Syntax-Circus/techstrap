using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Intake;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real submit handler against real Postgres and real local storage, including the races a pooled web host produces.</summary>
public sealed class SubmitTicketIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres), IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-submit-" + Guid.NewGuid().ToString("N"));

    private sealed record Seed(Guid ProductId, Guid ApiKeyId, Guid OtherApiKeyId);

    private sealed class ThrowingOutbox : IEmailOutbox
    {
        public void Enqueue(EmailOutboxItem item) => throw new InvalidOperationException("outbox unavailable");
    }

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PortalLinkOptions.PublicUrlKey] = "https://help.test",
                    ["Storage:Local:RootPath"] = _root,
                })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddScoped<ISubmitTicketRequestHandler, SubmitTicketRequestHandler>();
            extra?.Invoke(services);
        });

    private static async Task<Seed> SeedAsync(PersistenceTestHost host)
    {
        Guid productId = default, keyId = default, otherKeyId = default;
        (await host.CommitAsync(sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            products.Add(product);
            var key = ProductApiKey.Create(product.Id, ApiKeyKind.Trusted, "tsk_aaaa", "hash-a", "a", host.Clock).Value;
            var other = ProductApiKey.Create(product.Id, ApiKeyKind.Trusted, "tsk_bbbb", "hash-b", "b", host.Clock).Value;
            products.AddApiKey(key);
            products.AddApiKey(other);
            (productId, keyId, otherKeyId) = (product.Id, key.Id, other.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new Seed(productId, keyId, otherKeyId);
    }

    private static SubmitTicketRequest Request(string email) =>
        new(email, "Pat", "Cannot log in", "It fails with an error.", null, null);

    private static SubmitTicketContext WebContext(IReadOnlyList<IncomingAttachment>? files = null) =>
        new(IntakeChannel.Web, "orbitly", null, null, false, false, files ?? [], null);

    private static SubmitTicketContext ApiContext(Seed seed, string? idempotencyKey, Guid? apiKeyId = null) =>
        new(IntakeChannel.Api, null, seed.ProductId, apiKeyId ?? seed.ApiKeyId, true, false, [], idempotencyKey);

    private static IncomingAttachment PngFile() => new("shot.png", "image/png", Png.Length, new MemoryStream(Png));

    private static async Task<Result<SubmitTicketResponse>> SubmitAsync(PersistenceTestHost host, SubmitTicketRequest request, SubmitTicketContext context)
    {
        await using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISubmitTicketRequestHandler>().HandleAsync(request, context, Ct);
    }

    /// <summary>Starts every submission together behind one gate so the requests overlap instead of running back to back.</summary>
    private static async Task<T[]> RunTogetherAsync<T>(int count, Func<int, Task<T>> work)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await work(i);
        }, Ct)).ToArray();
        await Task.Delay(200, Ct);
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private async Task<long> ScalarAsync(string sql) => Convert.ToInt64(await ExecuteScalarAsync(sql));

    private async Task<string> TextAsync(string sql) => (string)(await ExecuteScalarAsync(sql))!;

    private async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }

    private string[] FilesUnderRoot() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task One_submission_writes_requester_ticket_message_event_token_and_outbox_together()
    {
        await using var host = NewHost();
        await SeedAsync(host);

        var result = await SubmitAsync(host, Request("pat@example.com"), WebContext([PngFile()]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-1");
        result.Value.ViewUrl.ShouldBeNull();
        (await ScalarAsync("SELECT count(*) FROM requesters")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM tickets WHERE number = 'ORB-1'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM attachments")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'Created'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'MessageAdded'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens WHERE token_hash LIKE 'sha256:%'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation' AND payload::text LIKE '%https://help.test/t/%'")).ShouldBe(1);
        FilesUnderRoot().Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_after_creation_leaves_no_rows_and_no_orphan_file()
    {
        await using var host = NewHost(services => services.AddScoped<IEmailOutbox, ThrowingOutbox>());
        await SeedAsync(host);

        await Should.ThrowAsync<InvalidOperationException>(() => SubmitAsync(host, Request("pat@example.com"), WebContext([PngFile()])));

        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM requesters")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        FilesUnderRoot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Twenty_concurrent_submissions_get_distinct_sequential_numbers()
    {
        await using var host = NewHost();
        await SeedAsync(host);

        var results = await RunTogetherAsync(20, i => SubmitAsync(host, Request($"user{i}@example.com"), WebContext()));

        results.ShouldAllBe(r => r.IsSuccess);
        results.Select(r => r.Value.TicketNumber).Order().ShouldBe(Enumerable.Range(1, 20).Select(n => $"ORB-{n}").Order());
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(20);
    }

    [Fact]
    public async Task Two_first_submissions_from_one_new_email_share_one_requester()
    {
        await using var host = NewHost();
        await SeedAsync(host);
        var emails = new[] { "New@Example.com", "new@example.com" };

        var results = await RunTogetherAsync(2, i => SubmitAsync(host, Request(emails[i]), WebContext()));

        results.ShouldAllBe(r => r.IsSuccess);
        (await ScalarAsync("SELECT count(*) FROM requesters")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
    }

    [Fact]
    public async Task Two_concurrent_requests_with_one_idempotency_key_create_one_ticket()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        var results = await RunTogetherAsync(2, _ => SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42")));

        results.ShouldAllBe(r => r.IsSuccess);
        results[0].Value.TicketNumber.ShouldBe(results[1].Value.TicketNumber);
        results.ShouldAllBe(r => r.Value.ViewUrl != null && r.Value.ViewUrl.StartsWith("https://help.test/t/"));
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await TextAsync("SELECT response::text FROM intake_idempotency_keys")).ShouldNotContain("/t/");
    }

    [Fact]
    public async Task The_same_idempotency_key_on_another_api_key_creates_a_second_ticket()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        var first = await SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42"));
        var second = await SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42", seed.OtherApiKeyId));

        first.Value.TicketNumber.ShouldBe("ORB-1");
        second.Value.TicketNumber.ShouldBe("ORB-2");
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
    }

    [Fact]
    public async Task An_expired_idempotency_key_creates_a_new_ticket()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        var first = await SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42"));
        host.Clock.Advance(TimeSpan.FromHours(25));
        var second = await SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42"));

        first.Value.TicketNumber.ShouldBe("ORB-1");
        second.Value.TicketNumber.ShouldBe("ORB-2");
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
    }
}
