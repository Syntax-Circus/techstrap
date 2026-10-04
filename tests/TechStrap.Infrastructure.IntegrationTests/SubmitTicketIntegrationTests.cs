using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Intake;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Tickets;

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

    /// <summary>Lets exactly two requests meet at a point. A timeout fails the test instead of hanging it.</summary>
    private sealed class Rendezvous
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _both.SetResult();
            }

            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
    }

    private sealed class BeginCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>Gates the first requester lookup of each request (one scope per request) until both requests have read.</summary>
    private sealed class GatedRequesterRepository(IRequesterRepository inner, Rendezvous rendezvous) : IRequesterRepository
    {
        private bool _gated;

        public Task<Requester?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);

        public async Task<Requester?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        {
            var found = await inner.GetByEmailAsync(email, cancellationToken);
            if (!_gated)
            {
                _gated = true;
                await rendezvous.ArriveAsync();
            }

            return found;
        }

        public void Add(Requester requester) => inner.Add(requester);

        public void Update(Requester requester) => inner.Update(requester);
    }

    /// <summary>Gates the first idempotency lookup of each request until both requests have read.</summary>
    private sealed class GatedIdempotencyStore(IIntakeIdempotencyStore inner, Rendezvous rendezvous) : IIntakeIdempotencyStore
    {
        private bool _gated;

        public async Task<IntakeIdempotencyEntry?> FindAsync(Guid apiKeyId, string idempotencyKey, CancellationToken cancellationToken)
        {
            var found = await inner.FindAsync(apiKeyId, idempotencyKey, cancellationToken);
            if (!_gated)
            {
                _gated = true;
                await rendezvous.ArriveAsync();
            }

            return found;
        }

        public void Add(Guid apiKeyId, string idempotencyKey, Guid ticketId, string responseJson, DateTimeOffset createdAt) =>
            inner.Add(apiKeyId, idempotencyKey, ticketId, responseJson, createdAt);

        public void Remove(IntakeIdempotencyEntry entry) => inner.Remove(entry);

        public Task<int> PruneAsync(DateTimeOffset olderThan, int limit, CancellationToken cancellationToken) => inner.PruneAsync(olderThan, limit, cancellationToken);
    }

    private sealed class CountingUnitOfWork(IUnitOfWork inner, BeginCounter counter) : IUnitOfWork
    {
        public Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken)
        {
            counter.Increment();
            return inner.BeginAsync(cancellationToken);
        }
    }

    /// <summary>Replaces the registered <typeparamref name="TService"/> with a decorator built around the implementation it had.</summary>
    private static void Decorate<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> decorate)
        where TService : class
    {
        var original = services.Last(d => d.ServiceType == typeof(TService));
        services.AddScoped(sp => decorate((TService)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), sp));
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
            services.AddTechStrapTicketOperations(configuration);
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
    public async Task A_submission_commits_one_new_ticket_alert_for_an_opted_in_agent()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);
        (await host.CommitAsync(async sp =>
        {
            var agent = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
            var agents = sp.GetRequiredService<IAgentRepository>();
            agents.Add(agent);
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(agent.Id, seed.ProductId, true), Ct);
        })).IsSuccess.ShouldBeTrue();

        (await SubmitAsync(host, Request("pat@example.com"), WebContext())).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'new-ticket-alert'")).ShouldBe(1);
        (await TextAsync("SELECT to_address FROM email_outbox WHERE kind = 'new-ticket-alert'")).ShouldBe("sam.taylor@techstrap.test");
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
        // The allocator's counter write is the one real statement made before the throw: it must have rolled back with the scope.
        (await ScalarAsync("SELECT count(*) FROM product_ticket_sequences")).ShouldBe(0);

        await using var healthy = NewHost();
        var retry = await SubmitAsync(healthy, Request("pat@example.com"), WebContext());
        retry.IsSuccess.ShouldBeTrue();
        retry.Value.TicketNumber.ShouldBe("ORB-1");
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
        var rendezvous = new Rendezvous();
        var begins = new BeginCounter();
        await using var host = NewHost(services =>
        {
            Decorate<IRequesterRepository>(services, (inner, _) => new GatedRequesterRepository(inner, rendezvous));
            Decorate<IUnitOfWork>(services, (inner, _) => new CountingUnitOfWork(inner, begins));
        });
        await SeedAsync(host);
        var seeded = begins.Count;
        var emails = new[] { "New@Example.com", "new@example.com" };

        var results = await RunTogetherAsync(2, i => SubmitAsync(host, Request(emails[i]), WebContext()));

        results.ShouldAllBe(r => r.IsSuccess);
        (await ScalarAsync("SELECT count(*) FROM requesters")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
        // Both read "no requester" before either wrote: the loser hit the unique index and retried once (2 + 1 attempts).
        (begins.Count - seeded).ShouldBe(3);
    }

    [Fact]
    public async Task Two_concurrent_requests_with_one_idempotency_key_create_one_ticket()
    {
        var rendezvous = new Rendezvous();
        var begins = new BeginCounter();
        await using var host = NewHost(services =>
        {
            Decorate<IIntakeIdempotencyStore>(services, (inner, _) => new GatedIdempotencyStore(inner, rendezvous));
            Decorate<IUnitOfWork>(services, (inner, _) => new CountingUnitOfWork(inner, begins));
        });
        var seed = await SeedAsync(host);
        var seeded = begins.Count;

        var results = await RunTogetherAsync(2, _ => SubmitAsync(host, Request("pat@example.com"), ApiContext(seed, "order-42")));

        results.ShouldAllBe(r => r.IsSuccess);
        results[0].Value.TicketNumber.ShouldBe(results[1].Value.TicketNumber);
        results.ShouldAllBe(r => r.Value.ViewUrl != null && r.Value.ViewUrl.StartsWith("https://help.test/t/"));
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await TextAsync("SELECT response::text FROM intake_idempotency_keys")).ShouldNotContain("/t/");
        results[0].Value.ViewUrl.ShouldNotBe(results[1].Value.ViewUrl);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(2);
        // Both missed the key before either committed: the loser retried once and replayed the winner (2 + 1 attempts).
        (begins.Count - seeded).ShouldBe(3);
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
