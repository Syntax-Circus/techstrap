using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The client's own behavior over a scripted connection: one start, the state it reports, the token it hands the hub, the reconnect it asks for, what it re-joins and re-announces afterwards, the
/// duplicates it drops, and a disposal that stops everything. A failure of the hub never reaches a caller.
/// </summary>
public sealed class SignalRTicketLiveClientTests
{
    private readonly FakeTimeProvider _time = new(At);
    private readonly FakeLiveConnectionFactory _factory = new();
    private readonly IUserAccessTokenProvider _tokens = Substitute.For<IUserAccessTokenProvider>();
    private readonly SessionExpiry _expiry = new();
    private readonly ListLogger<SignalRTicketLiveClient> _log = new();

    public SignalRTicketLiveClientTests() => _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("token-1"));

    private SignalRTicketLiveClient NewClient(ILiveConnectionFactory? factory = null) => new(factory ?? _factory, _tokens, _expiry, _time, _log);

    private static FakeLiveConnection FailingConnection(int failStarts) =>
        new(new LiveConnectionOptions(() => Task.FromResult<string?>("t"), new LiveRetryPolicy(() => false))) { FailStarts = failStarts };

    /// <summary>Waits for a condition on another thread, advancing the fake clock a second at a time (a retry delay is a timer on it). A ceiling, not a measurement.</summary>
    private async Task UntilAsync(Func<bool> condition)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow.AddSeconds(20); // below the tests' own 30 s Timeout, so the named assertion below is what fails
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            (DateTime.UtcNow < deadline).ShouldBeTrue("the condition was not reached");
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5, ct);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Start_connects_once_however_many_callers_ask_and_reports_connecting_then_connected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;
        client.State.ShouldBe(LiveConnectionState.Disconnected);

        // The token is held back so the first start is still in flight while the other callers arrive; all three begin together.
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<string?>(gate.Task));
        using var together = new Barrier(3);
        var callers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            together.SignalAndWait(ct);
            return client.StartAsync(ct);
        }, ct)).ToArray();
        await UntilAsync(() => _factory.Created.Count == 1 && _factory.Only.StartCalls == 1);
        gate.SetResult("token-1");
        await Task.WhenAll(callers);
        await client.StartAsync(ct);

        _factory.Created.Count.ShouldBe(1);
        _factory.Only.StartCalls.ShouldBe(1);
        states.ShouldBe([LiveConnectionState.Connecting, LiveConnectionState.Connected]);
        client.State.ShouldBe(LiveConnectionState.Connected);
        client.IsEnabled.ShouldBeTrue();
    }

    [Fact(Timeout = 30000)]
    public async Task Nothing_is_created_before_the_first_start()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();

        await client.JoinTicketAsync(TicketId, ct);
        await client.SetComposingAsync(TicketId, true, ct);

        _factory.Created.ShouldBeEmpty();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task The_token_is_asked_for_on_every_connection_attempt_and_never_cached()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("token-1"), new ValueTask<string?>("token-2"));
        await using var client = NewClient();
        await client.StartAsync(ct);

        (await _factory.Only.Options.AccessToken()).ShouldBe("token-2");
        await _tokens.Received(2).GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 30000)]
    public async Task A_null_token_stops_the_connection_for_good_with_no_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>((string?)null));
        await using var client = NewClient();

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Disconnected);
        _time.Advance(TimeSpan.FromMinutes(5));
        _factory.Only.StartCalls.ShouldBe(1);
        _factory.Only.Options.RetryPolicy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task A_token_provider_that_throws_is_retried_and_its_message_is_never_logged()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("secret-token-material"),
            _ => new ValueTask<string?>("token-2"));
        await using var client = NewClient();

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Connected);
        _factory.Only.StartCalls.ShouldBe(2);
        _log.Messages.ShouldNotBeEmpty();
        _log.Messages.ShouldAllBe(message => !message.Contains("secret-token-material", StringComparison.Ordinal) && !message.Contains("token-", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    public async Task A_failed_first_start_retries_with_the_backoff_until_it_connects()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 3);
        await using var client = NewClient(new SingleConnectionFactory(connection));
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;

        var start = client.StartAsync(ct);
        await UntilAsync(() => start.IsCompleted);
        await start;

        connection.StartCalls.ShouldBe(4);
        states.ShouldBe([LiveConnectionState.Connecting, LiveConnectionState.Reconnecting, LiveConnectionState.Connected]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_lapsed_session_ends_the_start_loop()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 100);
        await using var client = NewClient(new SingleConnectionFactory(connection));

        var start = client.StartAsync(ct);
        await UntilAsync(() => connection.StartCalls >= 2);
        _expiry.Report();
        await UntilAsync(() => start.IsCompleted);
        await start;

        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact]
    public void The_retry_policy_never_gives_up_caps_its_delay_and_stops_for_a_lapsed_session()
    {
        var stop = false;
        var policy = new LiveRetryPolicy(() => stop);

        var delays = Enumerable.Range(0, 8).Select(attempt => policy.NextRetryDelay(new RetryContext { PreviousRetryCount = attempt })).ToList();
        delays.ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)]);
        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 10_000 }).ShouldBe(LiveRetryPolicy.MaxDelay);

        stop = true;
        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task Reconnect_events_move_the_state_and_a_reconnect_asks_pages_to_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var states = new List<LiveConnectionState>();
        var changes = new List<TicketChangedDto>();
        client.StateChanged += states.Add;
        client.TicketChanged += changes.Add;

        await _factory.Only.RaiseReconnectingAsync();
        client.State.ShouldBe(LiveConnectionState.Reconnecting);
        changes.ShouldBeEmpty();
        await _factory.Only.RaiseReconnectedAsync();

        client.State.ShouldBe(LiveConnectionState.Connected);
        states.ShouldBe([LiveConnectionState.Reconnecting, LiveConnectionState.Connected]);
        var resync = changes.ShouldHaveSingleItem();
        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.EventId.ShouldNotBe(Guid.Empty);
        resync.ActorAgentId.ShouldBeNull();

        await _factory.Only.RaiseClosedAsync();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task Joined_tickets_are_joined_again_after_every_connect_and_the_presence_is_announced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.JoinTicketAsync(TicketId, ct);
        await client.JoinTicketAsync(OtherTicketId, ct);
        await client.LeaveTicketAsync(OtherTicketId, ct);
        var presence = new List<TicketPresenceDto>();
        client.PresenceChanged += presence.Add;
        _factory.Created.ShouldBeEmpty();

        await client.StartAsync(ct);

        _factory.Only.Joined.ShouldBe([TicketId]);
        presence.Select(p => p.TicketId).ShouldBe([TicketId]);

        await _factory.Only.RaiseReconnectedAsync();

        _factory.Only.Joined.ShouldBe([TicketId, TicketId]);
        presence.Count.ShouldBe(2);
    }

    [Fact(Timeout = 30000)]
    public async Task A_join_while_connected_returns_the_presence_and_a_leave_stops_the_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var viewer = Viewer(ColleagueId, "Colleague");
        _factory.Only.JoinHandler = id => Task.FromResult(Presence(id, viewer));

        var presence = await client.JoinTicketAsync(TicketId, ct);
        await client.LeaveTicketAsync(TicketId, ct);
        await _factory.Only.RaiseReconnectedAsync();

        presence.ShouldNotBeNull().Viewers.ShouldBe([viewer]);
        _factory.Only.Left.ShouldBe([TicketId]);
        _factory.Only.Joined.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_ticket_the_hub_does_not_know_is_not_joined_again_and_nothing_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        _factory.Only.JoinHandler = _ => throw new HubException(TicketHubMessages.TicketNotFound);

        var presence = await client.JoinTicketAsync(TicketId, ct);
        await _factory.Only.RaiseReconnectedAsync();

        presence.ShouldBeNull();
        _factory.Only.Joined.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_failing_invoke_never_throws_into_the_caller_and_its_message_is_never_logged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        _factory.Only.JoinHandler = _ => throw new InvalidOperationException("boom");
        _factory.Only.InvokeFailure = new InvalidOperationException("boom");

        (await client.JoinTicketAsync(TicketId, ct)).ShouldBeNull();
        await client.LeaveTicketAsync(TicketId, ct);
        await client.SetComposingAsync(TicketId, true, ct);

        _factory.Only.Composing.ShouldBe([(TicketId, true)]);
        _log.Messages.ShouldAllBe(message => !message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    public async Task An_event_id_is_delivered_once_and_the_memory_of_ids_is_bounded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var seen = new List<Guid>();
        client.TicketChanged += change => seen.Add(change.EventId);
        var first = Guid.NewGuid();

        _factory.Only.RaiseChanged(Change(eventId: first));
        _factory.Only.RaiseChanged(Change(eventId: first));
        seen.ShouldBe([first]);

        for (var i = 0; i < SignalRTicketLiveClient.DeduplicationCapacity; i++)
        {
            _factory.Only.RaiseChanged(Change());
        }

        // The first id is older than the last 256 distinct ones: forgotten, so it is delivered again; the newest is still remembered.
        _factory.Only.RaiseChanged(Change(eventId: first));
        seen.Count(id => id == first).ShouldBe(2);
        var newest = seen[^2];
        _factory.Only.RaiseChanged(Change(eventId: newest));
        seen.Count(id => id == newest).ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task A_throwing_subscriber_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var received = new List<TicketPresenceDto>();
        var changes = new List<TicketChangedDto>();
        client.PresenceChanged += _ => throw new InvalidOperationException("subscriber failed");
        client.PresenceChanged += received.Add;
        client.TicketChanged += _ => throw new InvalidOperationException("subscriber failed");
        client.TicketChanged += changes.Add;

        _factory.Only.RaisePresence(Presence(TicketId));
        _factory.Only.RaiseChanged(Change());

        received.Count.ShouldBe(1);
        changes.Count.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_stops_the_connection_once_unhooks_its_events_and_ends_every_later_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await client.StartAsync(ct);
        var connection = _factory.Only;
        connection.HasSubscribers.ShouldBeTrue();
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;

        await client.DisposeAsync();
        await client.DisposeAsync();

        connection.DisposeCalls.ShouldBe(1);
        connection.HasSubscribers.ShouldBeFalse();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
        (await client.JoinTicketAsync(TicketId, ct)).ShouldBeNull();
        await client.SetComposingAsync(TicketId, true, ct);
        await client.StartAsync(ct);
        connection.Joined.ShouldBeEmpty();
        connection.Composing.ShouldBeEmpty();
        _factory.Created.Count.ShouldBe(1);
        states.ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_ends_a_start_that_is_waiting_to_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 100);
        var client = NewClient(new SingleConnectionFactory(connection));
        var start = client.StartAsync(ct);
        await UntilAsync(() => connection.StartCalls >= 2);

        await client.DisposeAsync();
        await start;

        var calls = connection.StartCalls;
        _time.Advance(TimeSpan.FromMinutes(5));
        connection.StartCalls.ShouldBe(calls);
        connection.DisposeCalls.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task A_disposal_that_lands_before_the_connection_is_published_disposes_that_connection_itself()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new BlockingFactory(ct);
        var client = NewClient(factory);
        var start = client.StartAsync(ct);
        factory.Entered.Wait(ct);

        // The connection exists, but the client has not stored it yet: DisposeAsync cannot see it.
        var dispose = client.DisposeAsync().AsTask();
        await UntilAsync(() => client.StartAsync(ct).IsCompleted);
        factory.Release.Set();
        await dispose;
        await start;

        var connection = factory.Connection.ShouldNotBeNull();
        connection.DisposeCalls.ShouldBe(1);
        connection.StartCalls.ShouldBe(0);
        connection.HasSubscribers.ShouldBeFalse();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task A_disposal_while_the_first_start_is_in_flight_ends_it_without_joining_or_reporting_a_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<string?>(gate.Task));
        var client = NewClient();
        await client.JoinTicketAsync(TicketId, ct);
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;
        var start = client.StartAsync(ct);
        await UntilAsync(() => _factory.Created.Count == 1 && _factory.Only.StartCalls == 1);

        var dispose = client.DisposeAsync().AsTask();
        gate.SetResult("token-1");
        await dispose;
        await start;

        var connection = _factory.Only;
        connection.Joined.ShouldBeEmpty();
        connection.DisposeCalls.ShouldBe(1);
        client.State.ShouldBe(LiveConnectionState.Disconnected);
        states.ShouldNotContain(LiveConnectionState.Connected);
    }

    [Fact(Timeout = 30000)]
    public async Task A_hub_callback_that_runs_after_the_disposal_changes_nothing_and_joins_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await client.StartAsync(ct);
        await client.JoinTicketAsync(TicketId, ct);
        var connection = _factory.Only;
        await client.DisposeAsync();

        await connection.RaiseStaleConnectionEventsAsync();

        client.State.ShouldBe(LiveConnectionState.Disconnected);
        connection.Joined.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_leave_that_lands_while_a_rejoin_is_in_flight_is_sent_again_after_it_so_no_ghost_viewer_stays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        await client.JoinTicketAsync(TicketId, ct);
        var connection = _factory.Only;
        var answer = new TaskCompletionSource<TicketPresenceDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.JoinHandler = _ => answer.Task;
        var announced = new List<TicketPresenceDto>();
        client.PresenceChanged += announced.Add;

        var reconnect = connection.RaiseReconnectedAsync();
        await UntilAsync(() => connection.Joined.Count == 2);
        await client.LeaveTicketAsync(TicketId, ct);
        answer.SetResult(new TicketPresenceDto(TicketId, []));
        await reconnect;

        // One leave from the page, one from the client after the join that raced it; the stale join announces nothing.
        connection.Left.ShouldBe([TicketId, TicketId]);
        announced.ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task A_ticket_left_before_a_rejoin_starts_is_not_joined_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        await client.JoinTicketAsync(TicketId, ct);
        var other = Guid.NewGuid();
        await client.JoinTicketAsync(other, ct);
        var connection = _factory.Only;
        connection.Joined.Clear();
        connection.JoinHandler = async id =>
        {
            // The first re-join is answered while the page leaves the second ticket.
            await client.LeaveTicketAsync(id == TicketId ? other : TicketId, ct);
            return new TicketPresenceDto(id, []);
        };

        await connection.RaiseReconnectedAsync();

        connection.Joined.Count.ShouldBe(1);
    }

    private sealed class BlockingFactory(CancellationToken cancellationToken) : ILiveConnectionFactory, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public FakeLiveConnection? Connection { get; private set; }

        public ILiveConnection Create(LiveConnectionOptions options)
        {
            Connection = new FakeLiveConnection(options);
            Entered.Set();
            Release.Wait(cancellationToken);
            return Connection;
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class SingleConnectionFactory(FakeLiveConnection connection) : ILiveConnectionFactory
    {
        public ILiveConnection Create(LiveConnectionOptions options) => connection;
    }
}
