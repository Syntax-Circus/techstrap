using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The two hosts wired as in production: the Worker publishes through NOTIFY, the Api listens and pushes to a hub client (D-018). The wiring tests need no database; the relay test shares
/// one database between a real Api host and a real Worker host.
/// </summary>
public sealed class NotifyRelayHostTests(TestPostgres postgres)
{
    private const string ListenerQuery = "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'";

    [Fact]
    public void The_api_listens_and_pushes_to_the_hub_and_the_worker_only_notifies()
    {
        using var api = new ApiFactory();
        using var worker = new WorkerFactory();

        api.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ShouldContain("TicketChangeListener");
        api.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("SignalRTicketChangeBroadcaster");
        worker.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ShouldNotContain("TicketChangeListener");
        worker.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("PgNotifyTicketChangeBroadcaster");
        worker.Services.GetService<ITicketPresenceStore>().ShouldBeNull();
    }

    [Fact(Timeout = 180000)]
    public async Task A_change_the_worker_publishes_reaches_a_hub_client_and_a_killed_listener_connection_ends_in_a_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var apiSettings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var api = new ApiFactory(settings: apiSettings);
        await using var worker = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = database.ConnectionString });
        await TicketTestData.SeedAsync(api, ct);
        await using var connection = HubTestSupport.Connect(api, HubTestSupport.AgentToken("sam", "Sam"));
        using var inbox = new HubTestSupport.Inbox<TicketChangedDto>(connection, TicketHubMethods.TicketChanged);
        await connection.StartAsync(ct);
        await connection.ReadyAsync(ct);
        await WaitForListenerAsync(database, expected: 1);
        var change = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-9", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, ct);

        (await inbox.NextAsync()).ShouldBe(change.ToDto());

        // Kill the listener's backend: it reconnects after its backoff (a real second) and tells the hub's clients to reload everything.
        await database.ExecuteAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '{TicketChangeNotify.ListenerApplicationName}' AND query LIKE 'LISTEN %'");
        var resync = await inbox.NextAsync();

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        await WaitForListenerAsync(database, expected: 1);
        var after = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-10", Guid.NewGuid(), TicketEventTypes.Assigned, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);
        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(after, ct);
        (await inbox.NextAsync()).EventId.ShouldBe(after.EventId);
    }

    private static async Task WaitForListenerAsync(ApiTestDatabase database, int expected)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (await database.ScalarAsync<long>(ListenerQuery) != expected)
        {
            await Task.Delay(50, linked.Token);
        }
    }
}
