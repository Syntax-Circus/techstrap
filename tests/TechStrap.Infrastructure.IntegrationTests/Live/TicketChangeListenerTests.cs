using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The Api's NOTIFY listener against real Postgres (D-007, D-018): a notification reaches the relay, a bad payload is dropped without ending the loop, a killed connection leads to a
/// reconnect and one Resync, the listener never notifies (so nothing echoes), and stopping is clean. The real relay handler runs; the hub is a recorder.
/// </summary>
public sealed class TicketChangeListenerTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class CapturingLogger : ILogger<TicketChangeListener>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
    }

    private sealed class ListenerHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public ListenerHost(string? connectionString)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITicketChangeBroadcaster>(Recorder);
            services.AddScoped<IRelayTicketChangeHandler, RelayTicketChangeHandler>();
            _provider = services.BuildServiceProvider();
            Listener = new TicketChangeListener(
                Options.Create(new DatabaseConnectionOptions { ConnectionString = connectionString }),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Clock,
                Log);
        }

        public RecordingBroadcaster Recorder { get; } = new();

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

        public CapturingLogger Log { get; } = new();

        public TicketChangeListener Listener { get; }

        public Task StartAsync(CancellationToken cancellationToken) => Listener.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Listener.StopAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Listener.StopAsync(CancellationToken.None);
            Listener.Dispose();
            await _provider.DisposeAsync();
        }
    }

    private static string Payload(Guid? ticketId = null, string kind = TicketChangeKinds.Updated) =>
        JsonSerializer.Serialize(
            new TicketChangedDto(Guid.NewGuid(), ticketId ?? Guid.NewGuid(), "ORB-5", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), kind),
            JsonSerializerOptions.Web);

    private Task<IReadOnlyList<int>> WaitForListenerAsync(Func<IReadOnlyList<int>, bool>? condition = null) =>
        NotifyTestSupport.UntilAsync(() => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString), pids => condition?.Invoke(pids) ?? pids.Count == 1);

    // ---- the relay ----------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task A_notification_reaches_the_relay_and_the_broadcaster_once()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var payload = Payload();

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, payload);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        var change = host.Recorder.Attempts.ShouldHaveSingleItem();
        change.ToDto().ShouldBe(JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!);
    }

    [Fact(Timeout = 120000)]
    public async Task A_bad_payload_is_dropped_without_its_text_in_the_log_and_the_loop_goes_on()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var oversize = new string('x', TicketLiveLimits.MaxChangePayloadBytes + 10);

        foreach (var bad in new[] { "secret-not-json", "{\"kind\":\"Updated\"}", Payload(kind: "Exploded"), oversize, "null" })
        {
            await NotifyTestSupport.NotifyAsync(Database.ConnectionString, bad);
        }

        var good = Payload();
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, good);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        host.Recorder.Attempts.ShouldHaveSingleItem().EventId.ShouldBe(JsonSerializer.Deserialize<TicketChangedDto>(good, JsonSerializerOptions.Web)!.EventId);
        var warnings = host.Log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
        warnings.Count.ShouldBe(5);
        warnings.ShouldAllBe(message => message.Contains("dropped"));
        host.Log.Entries.ShouldAllBe(entry => !entry.Message.Contains("secret-not-json") && !entry.Message.Contains("xxxxx"));
    }

    [Fact(Timeout = 120000)]
    public async Task Notifications_that_arrive_together_are_all_relayed_in_order()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var payloads = Enumerable.Range(0, 3).Select(_ => Payload()).ToList();

        await NotifyTestSupport.NotifyTogetherAsync(Database.ConnectionString, payloads);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);

        host.Recorder.Attempts.Select(change => change.ToDto().EventId).ShouldBe(
            payloads.Select(payload => JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!.EventId));
    }

    [Fact(Timeout = 120000)]
    public async Task A_hub_that_fails_does_not_end_the_loop()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        host.Recorder.Behaviour = (_, _) => throw new InvalidOperationException("hub down");

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
        host.Recorder.Behaviour = null;
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 2);

        host.Recorder.Attempts.Count.ShouldBe(2);
    }

    [Fact(Timeout = 120000)]
    public async Task The_listener_only_listens_so_nothing_echoes()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var first = Payload();
        var sentinel = Payload();

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, first);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, sentinel);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 2);

        // The probe is on the same channel: it saw the two notifications the test sent and nothing the listener might have sent back.
        (await probe.NextAsync()).ShouldBe(first);
        (await probe.NextAsync()).ShouldBe(sentinel);
        probe.Pending().ShouldBeEmpty();
    }

    // ---- reconnecting -------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 180000)]
    public async Task Killing_the_connection_leads_to_a_reconnect_and_exactly_one_resync_and_the_relay_still_works()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var first = (await WaitForListenerAsync()).Single();
        host.Recorder.Attempts.ShouldBeEmpty();

        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, first);

        // The backoff waits on the fake clock: move it on until the listener is back on a new backend.
        var second = (await NotifyTestSupport.UntilAsync(
            () => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString),
            pids => pids.Count == 1 && pids[0] != first,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)))).Single();
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        second.ShouldNotBe(first);
        host.Recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Resync);
        host.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("lost its connection"));

        // A good connection resets the backoff: losing it again waits the first delay again, not the next one.
        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, second);
        await NotifyTestSupport.UntilAsync(
            () => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString),
            pids => pids.Count == 1 && pids[0] != second,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)));
        var lost = host.Log.Entries.Where(entry => entry.Message.Contains("lost its connection")).Select(entry => entry.Message).ToList();
        lost.ShouldAllBe(message => message.Contains("reconnect in 1s"));

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);
        host.Recorder.Attempts.Select(change => change.Kind).ShouldBe([TicketChangeKinds.Resync, TicketChangeKinds.Resync, TicketChangeKinds.Updated]);
    }

    [Fact(Timeout = 180000)]
    public async Task A_database_that_cannot_be_reached_is_retried_on_the_backoff_and_never_throws_out_of_the_service()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Host = "127.0.0.1", Port = 1, Timeout = 2 }.ConnectionString;
        await using var host = new ListenerHost(unreachable);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await NotifyTestSupport.UntilAsync(
            () => host.Log.Entries.Count(entry => entry.Message.Contains("could not connect")) >= 3,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)));

        host.Recorder.Attempts.ShouldBeEmpty();
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Each failure in a row waits twice as long as the one before (the loop's own escalation, not just the BackoffFor arithmetic).
        var delays = host.Log.Entries.Where(entry => entry.Message.Contains("could not connect")).Take(3).Select(entry => entry.Message).ToList();
        delays[0].ShouldContain("try again in 1s");
        delays[1].ShouldContain("try again in 2s");
        delays[2].ShouldContain("try again in 4s");
    }

    [Fact(Timeout = 180000)]
    public async Task A_first_connect_that_failed_is_followed_by_a_resync_once_listening_and_a_clean_start_sends_none()
    {
        // A forwarder that is not up yet: the first attempts are refused, then it comes up and forwards to the real database.
        var real = new NpgsqlConnectionStringBuilder(Database.ConnectionString);
        var probePort = new TcpListener(IPAddress.Loopback, 0);
        probePort.Start();
        var port = ((IPEndPoint)probePort.LocalEndpoint).Port;
        probePort.Stop();
        var viaForwarder = new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Host = "127.0.0.1", Port = port, Timeout = 5 }.ConnectionString;
        await using var host = new ListenerHost(viaForwarder);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await NotifyTestSupport.UntilAsync(
            () => host.Log.Entries.Any(entry => entry.Message.Contains("could not connect")),
            between: () => { });
        using var forwarder = new TcpListener(IPAddress.Loopback, port);
        forwarder.Start();
        using var stopForwarding = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await forwarder.AcceptTcpClientAsync(stopForwarding.Token);
                    _ = Task.Run(async () =>
                    {
                        using var upstream = new TcpClient();
                        await upstream.ConnectAsync(real.Host!, real.Port, stopForwarding.Token);
                        using var owned = client;
                        await Task.WhenAny(
                            client.GetStream().CopyToAsync(upstream.GetStream(), stopForwarding.Token),
                            upstream.GetStream().CopyToAsync(client.GetStream(), stopForwarding.Token));
                    }, TestContext.Current.CancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1, between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)));
            host.Recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Resync);
        }
        finally
        {
            await stopForwarding.CancelAsync();
            forwarder.Stop();
            await accepting;
        }
    }

    [Fact(Timeout = 120000)]
    public async Task A_notification_that_arrives_while_listening_starts_is_relayed_without_waiting_for_another()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        var payload = Payload();
        host.Listener.AfterListening = async (connection, cancellationToken) =>
        {
            // Sent after the LISTEN, read by the listener's own next command: Npgsql queues it before the first wait.
            await NotifyTestSupport.NotifyAsync(Database.ConnectionString, payload);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
        };

        await host.StartAsync(TestContext.Current.CancellationToken);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        host.Recorder.Attempts.ShouldHaveSingleItem().ToDto().ShouldBe(JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!);
    }

    [Fact(Timeout = 120000)]
    public async Task Several_notifications_already_queued_before_the_first_wait_are_all_relayed_in_order()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        var payloads = Enumerable.Range(0, 3).Select(_ => Payload()).ToList();
        host.Listener.AfterListening = async (connection, cancellationToken) =>
        {
            await NotifyTestSupport.NotifyTogetherAsync(Database.ConnectionString, payloads);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
        };

        await host.StartAsync(TestContext.Current.CancellationToken);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);

        host.Recorder.Attempts.Select(change => change.ToDto().EventId).ShouldBe(
            payloads.Select(payload => JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!.EventId));
    }

    [Fact(Timeout = 120000)]
    public async Task A_relay_that_hangs_is_cut_off_and_the_loop_goes_on()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        host.Recorder.Behaviour = (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
        host.Recorder.Behaviour = null;
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 2);

        host.Recorder.Attempts.Count.ShouldBe(2);
    }

    [Fact]
    public void The_backoff_doubles_from_the_initial_delay_and_stops_at_the_maximum()
    {
        Enumerable.Range(0, 7).Select(attempt => TicketChangeListener.BackoffFor(attempt).TotalSeconds).ShouldBe([1d, 2d, 4d, 8d, 16d, 30d, 30d]);
        TicketChangeListener.BackoffFor(-3).ShouldBe(TicketChangeNotify.ReconnectInitialDelay);
        TicketChangeListener.BackoffFor(10_000).ShouldBe(TicketChangeNotify.ReconnectMaxDelay);
    }

    [Fact]
    public void The_listener_gets_its_own_unpooled_named_keepalive_connection()
    {
        var builder = new NpgsqlConnectionStringBuilder(TicketChangeListener.ListenerConnectionString("Host=db;Database=techstrap;Username=u;Password=p;Maximum Pool Size=5"));

        builder.Pooling.ShouldBeFalse();
        builder.KeepAlive.ShouldBe(TicketChangeNotify.KeepAliveSeconds);
        builder.KeepAlive.ShouldBeGreaterThan(0);
        builder.ApplicationName.ShouldBe(TicketChangeNotify.ListenerApplicationName);
        builder.Database.ShouldBe("techstrap");
        builder.Host.ShouldBe("db");
    }

    // ---- starting and stopping ----------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task Stopping_closes_the_connection_and_returns_promptly()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();

        await host.StopAsync(TestContext.Current.CancellationToken);

        await NotifyTestSupport.UntilAsync(() => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString), pids => pids.Count == 0);
    }

    [Fact(Timeout = 120000)]
    public async Task Stopping_while_waiting_to_reconnect_is_clean_too()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var pid = (await WaitForListenerAsync()).Single();
        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, pid);
        await NotifyTestSupport.UntilAsync(() => host.Log.Entries.Any(entry => entry.Message.Contains("lost its connection")));

        // The clock never moved, so the listener is inside its backoff delay.
        await host.StopAsync(TestContext.Current.CancellationToken);

        await NotifyTestSupport.UntilAsync(() => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString), pids => pids.Count == 0);
    }

    [Theory(Timeout = 60000)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_connection_string_the_listener_is_off_and_starts_and_stops_cleanly(string? connectionString)
    {
        await using var host = new ListenerHost(connectionString);

        await host.StartAsync(TestContext.Current.CancellationToken);

        // With nothing to listen to, the service's work ends by itself; the wait is on that, not on a sleep.
        await host.Listener.ExecuteTask!.WaitAsync(NotifyTestSupport.Patience, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        host.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("is off"));
        host.Recorder.Attempts.ShouldBeEmpty();
    }
}
