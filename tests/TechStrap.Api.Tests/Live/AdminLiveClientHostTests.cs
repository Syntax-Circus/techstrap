using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Live;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// PHASE-10b end to end (T17), with the Admin's REAL <see cref="SignalRTicketLiveClient"/> on one side and the real Api hub on the other, in one process: the Worker's real auto-close handler closes a Solved ticket in
/// a real Postgres, the Worker's publisher NOTIFYs, the Api's real listener relays, the hub pushes, and the client raises exactly one change. Only the transport is a test double (long polling over the test server's handler,
/// the way the 10a hub tests connect) and the token provider (a fixed test JWT in place of the circuit's OIDC token). This test lives in Api.Tests because it is the one project that references the Api, the Worker and the Admin.
/// </summary>
public sealed class AdminLiveClientHostTests(TestPostgres postgres)
{
    private const string ListenerQuery = "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'";

    private static SignalRTicketLiveClient NewClient(ApiFactory api, Func<string?> token, SessionExpiry? expiry = null) =>
        new(
            new HubLiveConnectionFactory(
                Microsoft.Extensions.Options.Options.Create(new ApiOptions { BaseUrl = api.Server.BaseAddress.ToString() }),
                http =>
                {
                    http.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                    http.Transports = HttpTransportType.LongPolling;
                }),
            new TestTokenProvider(token),
            expiry ?? new SessionExpiry(),
            TimeProvider.System,
            NullLogger<SignalRTicketLiveClient>.Instance);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        try
        {
            while (!await condition())
            {
                await Task.Delay(50, linked.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new ShouldAssertException($"Timed out waiting for {what}");
        }
    }

    private static Task<T> NextAsync<T>(Channel<T> channel)
    {
        return NextCoreAsync();

        async Task<T> NextCoreAsync()
        {
            using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await channel.Reader.ReadAsync(linked.Token);
        }
    }

    [Fact(Timeout = 240000)]
    public async Task A_ticket_the_worker_auto_closes_reaches_the_admin_client_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var apiSettings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var api = new ApiFactory(settings: apiSettings);
        await using var worker = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = database.ConnectionString });
        var seed = await TicketTestData.SeedAsync(api, ct);
        var ticketId = seed.Tickets[0].Id;

        // A Solved ticket whose solve is eight days old (the Worker closes after seven): solved through the real endpoint, then backdated in the database.
        using (var sam = TicketTestData.AgentClient(api, "sam"))
        {
            var solved = await sam.PutAsJsonAsync($"/api/tickets/{ticketId}/status", new ChangeTicketStatusRequest(TicketStatuses.Solved, await TicketTestData.VersionAsync(sam, ticketId)), ct);
            solved.EnsureSuccessStatusCode();
        }

        await database.ExecuteAsync($"UPDATE tickets SET solved_at = now() - interval '8 days' WHERE id = '{ticketId}'");

        await using var client = NewClient(api, () => HubTestSupport.AgentToken("sam", "Sam"));
        var changes = Channel.CreateUnbounded<TicketChangedDto>();
        client.TicketChanged += change => changes.Writer.TryWrite(change);
        await client.StartAsync(ct);
        client.State.ShouldBe(LiveConnectionState.Connected);

        // A answered call proves the connection is in the queue group (StartAsync returns before OnConnectedAsync completes); the join of the real ticket returns its presence.
        (await client.JoinTicketAsync(ticketId, ct)).ShouldNotBeNull().TicketId.ShouldBe(ticketId);
        await WaitForAsync(async () => await database.ScalarAsync<long>(ListenerQuery) == 1, "the Api listener is listening");

        await using (var scope = worker.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Closed.ShouldBe(1);
        }

        var closed = await NextAsync(changes);
        closed.TicketId.ShouldBe(ticketId);
        closed.TicketNumber.ShouldBe(seed.Tickets[0].Number.ToString());
        closed.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        closed.Kind.ShouldBe(TicketChangeKinds.Updated);
        closed.ActorAgentId.ShouldBeNull("the Worker acts as the system, so no agent's page treats it as their own change");
        LiveChangeRules.IsOwn(closed, seed.Sam.Id).ShouldBeFalse();
        LiveChangeRules.Concerns(closed, ticketId).ShouldBeTrue();

        // No second copy follows. A barrier proves it: a later message on the same connection arrives after every earlier one, so once it is here nothing else for the ticket is coming.
        var barrier = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-99", Guid.NewGuid(), TicketEventTypes.Assigned, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);
        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(barrier, ct);
        var received = new List<TicketChangedDto>();
        TicketChangedDto next;
        do
        {
            next = await NextAsync(changes);
            received.Add(next);
        }
        while (next.EventId != barrier.EventId);

        received.Where(change => change.TicketId == ticketId).ShouldBeEmpty("auto-close must reach the client exactly once");
        (await database.ScalarAsync<string>($"SELECT status FROM tickets WHERE id = '{ticketId}'")).ShouldBe(TicketStatuses.Closed);
    }

    [Fact(Timeout = 120000)]
    public async Task A_ticket_the_hub_does_not_know_is_not_joined_and_nothing_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var api = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(api, ct);
        await using var client = NewClient(api, () => HubTestSupport.AgentToken("sam", "Sam"));
        await client.StartAsync(ct);

        (await client.JoinTicketAsync(Guid.NewGuid(), ct)).ShouldBeNull();
        await client.SetComposingAsync(Guid.NewGuid(), true, ct);
        await client.LeaveTicketAsync(Guid.NewGuid(), ct);

        client.State.ShouldBe(LiveConnectionState.Connected);
    }

    [Fact(Timeout = 120000)]
    public async Task Without_a_token_the_hub_refuses_and_the_client_stops_for_good()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new ApiFactory();
        var asked = 0;
        await using var client = NewClient(api, () =>
        {
            Interlocked.Increment(ref asked);
            return null;
        });

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Disconnected);
        asked.ShouldBe(1);
    }

    [Fact(Timeout = 180000)]
    public async Task When_the_token_expires_the_hub_closes_the_connection_and_the_reconnect_uses_a_fresh_token_and_asks_for_a_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var api = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(api, ct);
        var tokensIssued = 0;
        await using var client = NewClient(api, () =>
        {
            // The first token lives four seconds. This depends on the REAL clock: the Api closes the connection when the token's exp passes (CloseOnAuthenticationExpiration), and the test waits for that. Every later token is fresh.
            return Interlocked.Increment(ref tokensIssued) == 1
                ? HubTestSupport.AgentToken("sam", "Sam", expires: DateTime.UtcNow.AddSeconds(4))
                : HubTestSupport.AgentToken("sam", "Sam");
        });
        var changes = Channel.CreateUnbounded<TicketChangedDto>();
        client.TicketChanged += change => changes.Writer.TryWrite(change);
        var reconnecting = 0;
        client.StateChanged += state =>
        {
            if (state == LiveConnectionState.Reconnecting)
            {
                Interlocked.Exchange(ref reconnecting, 1);
            }
        };
        await client.StartAsync(ct);

        var resync = await NextAsync(changes);

        // The Resync is raised only by a completed reconnect. The client asks the provider again rather than reusing the first token
        // (a client that cached it would issue one token only). The Api's JWT clock skew would still accept the just-expired first
        // token, so this cannot prove the reconnect needed the fresh one; it pins that the client never caches.
        // Counting tokens at the moment "Reconnecting" is seen is racy: the first retry has no delay and may fetch its token first.
        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        Volatile.Read(ref reconnecting).ShouldBe(1, "the connection was seen reconnecting");
        Volatile.Read(ref tokensIssued).ShouldBeGreaterThan(1, "a fresh token was issued after the first one");
        await WaitForAsync(() => Task.FromResult(client.State == LiveConnectionState.Connected), "connected again");
    }

    private sealed class TestTokenProvider(Func<string?> token) : IUserAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(token());
    }
}
