using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.DeadLetters;
using TechStrap.Application.Email;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Attachments;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real dead-letter handlers against Postgres and the real drain handler, with only the SMTP sender faked (D-039).</summary>
public sealed class DeadLetterIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string WorkerId = "w1";

    private sealed class StubClaims(AgentClaims claims) : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = claims;
    }

    private sealed class RecordingSender : IOutboundEmailSender
    {
        public ConcurrentQueue<OutboundEmail> Sent { get; } = new();

        public Task<Result> SendAsync(OutboundEmail email, CancellationToken cancellationToken)
        {
            Sent.Enqueue(email);
            return Task.FromResult(Result.Success());
        }
    }

    private PersistenceTestHost CreateHost(RecordingSender sender)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "localhost",
            ["Email:Smtp:Port"] = "1025",
            ["Email:Smtp:TlsMode"] = "None",
            ["Email:Smtp:MaxRetryAttempts"] = "1",
            ["Email:Smtp:RetryMode"] = "TransientOnly",
            ["Email:Smtp:TotalSendTimeout"] = "00:00:10",
            ["Email:Smtp:DefaultFrom"] = "support@example.test",
        }).Build();
        return new PersistenceTestHost(Database, configure: services =>
        {
            services.AddSingleton<IOutboundEmailSender>(sender);
            services.AddTechStrapEmail(configuration);
            services.AddTechStrapProductLogoUrls(configuration);
        });
    }

    private static async Task<Guid> SeedAsync(PersistenceTestHost host)
    {
        var productId = Guid.Empty;
        var admin = Agent.Create("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            var branding = ProductBranding.Create("Orbitly Support", null, null, "help@orbitly.test", null).Value;
            var product = Product.Create("orbitly", "Orbitly", "ORB", branding, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            sp.GetRequiredService<IAgentRepository>().Add(admin);
            productId = product.Id;
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return productId;
    }

    private static async Task<Guid> QueueAsync(PersistenceTestHost host, Guid productId, string address)
    {
        var id = Guid.Empty;
        (await host.CommitAsync(sp =>
        {
            var payload = JsonSerializer.Serialize(
                new TicketConfirmationEmail("ORB-1001", "Cannot log in", "Pat", "https://portal.test/t/abc123", null),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var item = EmailOutboxItem.Enqueue(EmailTemplates.TicketConfirmation, address, payload, productId, null, host.Clock).Value;
            sp.GetRequiredService<IEmailOutbox>().Enqueue(item);
            id = item.Id;
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return id;
    }

    private static async Task DeadLetterAsync(PersistenceTestHost host, params Guid[] ids)
    {
        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            host.Clock.Advance(TimeSpan.FromHours(2));
            var claim = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ClaimBatchAsync(WorkerId, 20, TimeSpan.FromMinutes(5), Ct));
            foreach (var item in claim.Where(row => ids.Contains(row.Id)))
            {
                (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, WorkerId, "smtp-permanent", Ct))).IsSuccess.ShouldBeTrue();
            }
        }

        foreach (var id in ids)
        {
            (await RowAsync(host, id)).Status.ShouldBe(OutboxStatus.DeadLettered);
        }
    }

    private static async Task<Result> RetryAsync(PersistenceTestHost host, Guid id)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        return await new RetryDeadLetterRequestHandler(
            sp.GetRequiredService<IEmailOutboxStore>(), sp.GetRequiredService<IAdminEventRepository>(), sp.GetRequiredService<IAgentRepository>(),
            new StubClaims(new AgentClaims("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin)), sp.GetRequiredService<IUnitOfWork>(), host.Clock)
            .HandleAsync(id, Ct);
    }

    private static async Task<Result> DiscardAsync(PersistenceTestHost host, Guid id)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        return await new DiscardDeadLetterRequestHandler(
            sp.GetRequiredService<IEmailOutboxStore>(), sp.GetRequiredService<IAdminEventRepository>(), sp.GetRequiredService<IAgentRepository>(),
            new StubClaims(new AgentClaims("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin)), sp.GetRequiredService<IUnitOfWork>(), host.Clock)
            .HandleAsync(id, Ct);
    }

    private static async Task<DrainResult> DrainAsync(PersistenceTestHost host, string workerId)
    {
        await using var scope = host.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDrainEmailOutboxHandler>().HandleAsync(workerId, Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static Task<EmailOutboxItem> RowAsync(PersistenceTestHost host, Guid id) =>
        host.ReadAsync(async sp => (await sp.GetRequiredService<IEmailOutboxStore>().GetAsync(id, Ct))!);

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task A_retried_dead_letter_is_drained_on_the_next_worker_pass_and_a_discarded_one_never_is()
    {
        var sender = new RecordingSender();
        await using var host = CreateHost(sender);
        var productId = await SeedAsync(host);
        var rowA = await QueueAsync(host, productId, "pat-a@example.test");
        var rowB = await QueueAsync(host, productId, "pat-b@example.test");
        await DeadLetterAsync(host, rowA, rowB);

        (await RetryAsync(host, rowA)).IsSuccess.ShouldBeTrue();
        (await DiscardAsync(host, rowB)).IsSuccess.ShouldBeTrue();

        var drained = await DrainAsync(host, WorkerId);
        drained.Sent.ShouldBe(1);
        sender.Sent.ShouldHaveSingleItem();
        (await RowAsync(host, rowA)).Status.ShouldBe(OutboxStatus.Sent);
        (await RowAsync(host, rowB)).Status.ShouldBe(OutboxStatus.Discarded);
        (await ScalarAsync("SELECT count(*) FROM admin_events WHERE type IN ('DeadLetterRetried','DeadLetterDiscarded')")).ShouldBe(2);
    }

    [Fact]
    public async Task Retry_and_discard_of_a_live_row_change_nothing()
    {
        var sender = new RecordingSender();
        await using var host = CreateHost(sender);
        var productId = await SeedAsync(host);
        var sent = await QueueAsync(host, productId, "pat-b@example.test");
        (await DrainAsync(host, WorkerId)).Sent.ShouldBe(1);
        var pending = await QueueAsync(host, productId, "pat-a@example.test");

        foreach (var id in new[] { pending, sent })
        {
            var before = await RowAsync(host, id);
            var retry = await RetryAsync(host, id);
            var discard = await DiscardAsync(host, id);

            retry.Errors.ShouldHaveSingleItem().Code.ShouldBe("outbox-not-dead-lettered");
            discard.Errors.ShouldHaveSingleItem().Code.ShouldBe("outbox-not-dead-lettered");
            var after = await RowAsync(host, id);
            after.Status.ShouldBe(before.Status);
            after.Attempts.ShouldBe(before.Attempts);
        }

        (await ScalarAsync("SELECT count(*) FROM admin_events")).ShouldBe(0);
    }
}
