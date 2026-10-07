using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TechStrap.Application.Intake;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.AutoClose;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The Worker's side of the relay against real Postgres: the NOTIFY it sends has the right channel and a small, ids-only JSON payload, a worker-style commit notifies once per ticket
/// and a rollback never does, and the real auto-close handler produces the notification the Api will relay.
/// </summary>
public sealed class PgNotifyBroadcasterTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private PgNotifyTicketChangeBroadcaster NewBroadcaster() =>
        new(Options.Create(new DatabaseConnectionOptions { ConnectionString = Database.ConnectionString }));

    private PersistenceTestHost NewWorkerHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            // The Worker's database options are validated when something reads them, which needs the host environment a real host has.
            services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
            services.AddTechStrapNotifyBroadcaster();
            extra?.Invoke(services);
        });

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TechStrap.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static TicketChange Change() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ORB-42", Guid.NewGuid(), TicketEventTypes.StatusChanged, Guid.NewGuid(), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), TicketChangeKinds.Updated);

    [Fact(Timeout = 120000)]
    public async Task A_published_change_arrives_on_the_channel_as_small_ids_only_json()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var broadcaster = NewBroadcaster();
        var change = Change();

        await broadcaster.PublishAsync(change, TestContext.Current.CancellationToken);

        var payload = await probe.NextAsync();
        JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web).ShouldBe(change.ToDto());
        Encoding.UTF8.GetByteCount(payload).ShouldBeLessThan(8000);
        Encoding.UTF8.GetByteCount(payload).ShouldBeLessThanOrEqualTo(TicketLiveLimits.MaxChangePayloadBytes);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["eventId", "ticketId", "ticketNumber", "productId", "eventType", "actorAgentId", "occurredAt", "kind"]);
    }

    [Fact]
    public async Task A_change_that_is_over_the_cap_is_refused_before_it_is_sent()
    {
        await using var broadcaster = NewBroadcaster();

        await Should.ThrowAsync<InvalidOperationException>(() => broadcaster.PublishAsync(Change() with { TicketNumber = new string('X', 3000) }, Ct));
    }

    [Fact]
    public async Task Presence_is_not_sent_because_it_lives_in_the_api_process()
    {
        await using var broadcaster = NewBroadcaster();

        await broadcaster.PublishPresenceAsync(new TicketPresence(Guid.NewGuid(), []), Ct);
    }

    [Fact]
    public async Task Without_a_connection_string_publishing_throws_and_does_not_create_a_data_source()
    {
        await using var broadcaster = new PgNotifyTicketChangeBroadcaster(Options.Create(new DatabaseConnectionOptions()));

        await Should.ThrowAsync<InvalidOperationException>(() => broadcaster.PublishAsync(Change(), Ct));
    }

    [Fact(Timeout = 120000)]
    public async Task A_worker_style_commit_notifies_once_per_ticket_and_a_rollback_notifies_nothing()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = NewWorkerHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(TestContext.Current.CancellationToken);
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 700).Value, scenario.Acme.Id, scenario.Requester.Id, "Rolled back", TicketChannel.Web, null, false, host.Clock).Value;
            scope.ServiceProvider.GetRequiredService<ITicketRepository>().Add(ticket);
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var committed = await scenario.CreateTicketAsync("Committed");

        var payload = JsonSerializer.Deserialize<TicketChangedDto>(await probe.NextAsync(), JsonSerializerOptions.Web)!;
        payload.TicketId.ShouldBe(committed.Id);
        payload.Kind.ShouldBe(TicketChangeKinds.Created);
        payload.TicketNumber.ShouldBe(committed.Number.ToString());

        // Anything else would have been notified before this barrier.
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, "barrier");
        (await probe.NextAsync()).ShouldBe("barrier");
        probe.Pending().ShouldBeEmpty();
    }

    [Fact(Timeout = 120000)]
    public async Task The_real_auto_close_handler_notifies_a_status_change_with_no_agent_actor()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = NewWorkerHost(services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [PortalLinkOptions.PublicUrlKey] = "https://help.test", [AutoCloseOptions.DaysKey] = "7" })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddTechStrapAutoClose(configuration);
        });
        Guid productId = default, requesterId = default, ticketId = default;
        (await host.CommitAsync(services =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            services.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            services.GetRequiredService<IRequesterRepository>().Add(requester);
            (productId, requesterId) = (product.Id, requester.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        (await host.CommitAsync(async services =>
        {
            var number = (await services.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(productId, TestContext.Current.CancellationToken)).Value;
            var ticket = Ticket.Create(number, productId, requesterId, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(requesterId, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, Actor.ForAgent(Guid.NewGuid()), host.Clock).IsSuccess.ShouldBeTrue();
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            ticketId = ticket.Id;
        })).IsSuccess.ShouldBeTrue();
        _ = await probe.NextAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));

        await using var scope = host.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        var closed = JsonSerializer.Deserialize<TicketChangedDto>(await probe.NextAsync(), JsonSerializerOptions.Web)!;
        closed.TicketId.ShouldBe(ticketId);
        closed.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        closed.Kind.ShouldBe(TicketChangeKinds.Updated);
        closed.ActorAgentId.ShouldBeNull();
        probe.Pending().ShouldBeEmpty();
    }
}
