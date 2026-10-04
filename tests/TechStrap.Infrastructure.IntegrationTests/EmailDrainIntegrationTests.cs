using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TechStrap.Application.Email;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Email;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.IntegrationTests.Support;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real drain handler and SMTP adapter against a real Mailpit and Postgres (D-010, D-033).</summary>
[Collection(MailpitCollection.Name)]
public sealed class EmailDrainIntegrationTests(PostgresFixture postgres, MailpitContainer mailpit) : PostgresIntegrationTestBase(postgres)
{
    private const string PortalLink = "https://portal.test/t/abc123";
    private readonly CapturingLoggerProvider _logs = new();

    private PersistenceTestHost CreateHost(string? host = null, int? port = null, IDictionary<string, string?>? extra = null, Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = host ?? mailpit.SmtpHost,
            ["Email:Smtp:Port"] = (port ?? mailpit.SmtpPort).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Email:Smtp:TlsMode"] = "None",
            ["Email:Smtp:MaxRetryAttempts"] = "1",
            ["Email:Smtp:RetryMode"] = "TransientOnly",
            ["Email:Smtp:TotalSendTimeout"] = "00:00:10",
            ["Email:Smtp:DefaultFrom"] = "support@example.test",
        };
        foreach (var pair in extra ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new PersistenceTestHost(Database, configure: services =>
        {
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
            services.AddTechStrapEmail(configuration);
            configureServices?.Invoke(services, configuration);
        });
    }

    private static async Task<Guid> SeedProductAsync(PersistenceTestHost host)
    {
        var productId = Guid.Empty;
        (await host.CommitAsync(sp =>
        {
            var branding = ProductBranding.Create("Orbitly Support", null, null, "help@orbitly.test", null).Value;
            var product = Product.Create("orbitly", "Orbitly", "ORB", branding, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            productId = product.Id;
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return productId;
    }

    private static async Task<IReadOnlyList<Guid>> QueueAsync(PersistenceTestHost host, Guid productId, int count)
    {
        var ids = new List<Guid>();
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            for (var i = 0; i < count; i++)
            {
                var payload = JsonSerializer.Serialize(
                    new TicketConfirmationEmail($"ORB-{1000 + i}", $"Cannot log in {i}", "Pat", PortalLink, null),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var item = EmailOutboxItem.Enqueue(EmailTemplates.TicketConfirmation, $"pat{i}@example.test", payload, productId, null, host.Clock).Value;
                outbox.Enqueue(item);
                ids.Add(item.Id);
            }

            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return ids;
    }

    private static async Task<DrainResult> DrainAsync(PersistenceTestHost host, string workerId)
    {
        await using var scope = host.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDrainEmailOutboxHandler>().HandleAsync(workerId, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static async Task DrainUntilEmptyAsync(PersistenceTestHost host, string workerId)
    {
        while ((await DrainAsync(host, workerId)).Claimed > 0)
        {
        }
    }

    private static Task<EmailOutboxItem> RowAsync(PersistenceTestHost host, Guid id) =>
        host.ReadAsync(async sp => (await sp.GetRequiredService<IEmailOutboxStore>().GetAsync(id, TestContext.Current.CancellationToken))!);

    [Fact]
    public async Task One_hundred_queued_emails_drained_by_two_workers_arrive_once_each_with_the_outbox_id_as_message_id()
    {
        await mailpit.ClearAsync(TestContext.Current.CancellationToken);
        await using var host = CreateHost();
        var ids = await QueueAsync(host, await SeedProductAsync(host), 100);

        await Task.WhenAll(
            Task.Run(() => DrainUntilEmptyAsync(host, "w1"), TestContext.Current.CancellationToken),
            Task.Run(() => DrainUntilEmptyAsync(host, "w2"), TestContext.Current.CancellationToken));

        var messages = await mailpit.MessagesAsync(TestContext.Current.CancellationToken);
        messages.Count.ShouldBe(100);
        var expected = ids.Select(OutboundMessageIds.For).ToHashSet();
        var actual = messages.Select(m => m.MessageId).ToList();
        actual.Except(expected).ShouldBe([], "unexpected ids");
        expected.Except(actual).ShouldBe([], "missing ids");
        actual.Distinct().Count().ShouldBe(100);
        foreach (var id in ids)
        {
            var row = await RowAsync(host, id);
            row.Status.ShouldBe(OutboxStatus.Sent);
            row.Attempts.ShouldBe(1);
        }
    }

    [Fact]
    public async Task The_confirmation_carries_the_product_branding_and_the_portal_link()
    {
        await mailpit.ClearAsync(TestContext.Current.CancellationToken);
        await using var host = CreateHost();
        await QueueAsync(host, await SeedProductAsync(host), 1);

        await DrainUntilEmptyAsync(host, "w1");

        var message = (await mailpit.MessagesAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        message.FromName.ShouldBe("Orbitly Support");
        message.FromAddress.ShouldBe("help@orbitly.test");
        message.Subject.ShouldStartWith("[ORB-");
        (await mailpit.HtmlAsync(message.Id, TestContext.Current.CancellationToken)).ShouldContain(PortalLink);
    }

    [Fact]
    public async Task A_submitted_ticket_drains_into_one_mailpit_message_with_the_stored_link_and_message_id()
    {
        await mailpit.ClearAsync(TestContext.Current.CancellationToken);
        var storage = Path.Combine(Path.GetTempPath(), "techstrap-seam-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = CreateHost(
                extra: new Dictionary<string, string?>
                {
                    [PortalLinkOptions.PublicUrlKey] = "https://help.test",
                    ["Storage:Local:RootPath"] = storage,
                },
                configureServices: (services, configuration) =>
                {
                    services.AddTechStrapIntake(configuration);
                    services.AddTechStrapTicketOperations(configuration);
                    services.AddScoped<ISubmitTicketRequestHandler, SubmitTicketRequestHandler>();
                });
            await SeedProductAsync(host);

            SubmitTicketResponse response;
            await using (var scope = host.CreateScope())
            {
                var submit = await scope.ServiceProvider.GetRequiredService<ISubmitTicketRequestHandler>().HandleAsync(
                    new SubmitTicketRequest("pat@example.test", "Pat", "Cannot log in", "It fails with an error.", null, null),
                    new SubmitTicketContext(IntakeChannel.Web, "orbitly", null, null, false, false, [], null),
                    TestContext.Current.CancellationToken);
                submit.IsSuccess.ShouldBeTrue();
                response = submit.Value;
            }

            (await DrainAsync(host, "w1")).Sent.ShouldBe(1);

            Guid outboxId;
            string payload;
            await using (var connection = new NpgsqlConnection(Database.ConnectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT id, payload::text FROM email_outbox";
                await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
                (outboxId, payload) = (reader.GetGuid(0), reader.GetString(1));
                (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
            }

            var link = JsonDocument.Parse(payload).RootElement.GetProperty("portalLink").GetString()!;
            link.ShouldContain("/t/");
            var message = (await mailpit.MessagesAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
            message.Subject.ShouldStartWith($"[{response.TicketNumber}]");
            message.MessageId.ShouldBe(OutboundMessageIds.For(outboxId));
            (await mailpit.HtmlAsync(message.Id, TestContext.Current.CancellationToken)).ShouldContain(link);
        }
        finally
        {
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_dead_smtp_server_retries_then_dead_letters_without_leaking_details()
    {
        await mailpit.ClearAsync(TestContext.Current.CancellationToken);
        var secrets = new[] { "dead-smtp.invalid", "smtp-user-canary", "smtp-pass-canary" };
        await using var host = CreateHost("dead-smtp.invalid", 2525, new Dictionary<string, string?>
        {
            ["Email:Smtp:Username"] = "smtp-user-canary",
            ["Email:Smtp:Password"] = "smtp-pass-canary",
        });
        var id = (await QueueAsync(host, await SeedProductAsync(host), 1)).Single();

        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            var drained = await DrainAsync(host, "w1");
            drained.Claimed.ShouldBe(1);
            drained.Failed.ShouldBe(1);
            host.Clock.Advance(OutboxRetryPolicy.MaxDelay);
        }

        var row = await RowAsync(host, id);
        row.Status.ShouldBe(OutboxStatus.DeadLettered);
        row.Attempts.ShouldBe(OutboxRetryPolicy.MaxAttempts);
        new[]
        {
            EmailSendFailures.Transient, EmailSendFailures.Permanent, EmailSendFailures.Authentication, EmailSendFailures.Timeout, EmailSendFailures.Unknown,
        }.ShouldContain(row.LastError);
        (await DrainAsync(host, "w1")).Claimed.ShouldBe(0);
        (await mailpit.MessagesAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();

        _logs.Lines.ShouldNotBeEmpty();
        foreach (var secret in secrets)
        {
            row.LastError!.ShouldNotContain(secret);
            _logs.Lines.ShouldNotContain(line => line.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task A_row_claimed_by_a_crashed_worker_is_sent_after_its_lease_expires()
    {
        await mailpit.ClearAsync(TestContext.Current.CancellationToken);
        await using var host = CreateHost();
        var id = (await QueueAsync(host, await SeedProductAsync(host), 1)).Single();
        await using (var scope = host.CreateScope())
        {
            var claimed = await scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>()
                .ClaimBatchAsync("crashed", 20, TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
            claimed.ShouldHaveSingleItem();
        }

        (await DrainAsync(host, "w2")).Claimed.ShouldBe(0);
        (await mailpit.MessagesAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();

        host.Clock.Advance(TimeSpan.FromMinutes(3));
        (await DrainAsync(host, "w2")).Sent.ShouldBe(1);

        (await mailpit.MessagesAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem().MessageId.ShouldBe(OutboundMessageIds.For(id));
        (await RowAsync(host, id)).Status.ShouldBe(OutboxStatus.Sent);
    }

    /// <summary>Records every formatted line, with the exception text, from every logger the host creates.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue($"{category}: {formatter(state, exception)}");
                if (exception is not null)
                {
                    lines.Enqueue(exception.ToString());
                }
            }
        }
    }
}
