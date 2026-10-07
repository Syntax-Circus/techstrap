using Microsoft.Extensions.Time.Testing;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>Presence rules without a database: what counts as a change, the composing lease on a fake clock, one entry per agent, and safety under contention.</summary>
public sealed class InMemoryTicketPresenceStoreTests
{
    private static readonly Guid Ticket1 = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid Ticket2 = Guid.Parse("0197f2a0-0000-7000-8000-000000000002");
    private static readonly Guid Sam = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Kim = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a2");
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTicketPresenceStore _store;

    public InMemoryTicketPresenceStoreTests() => _store = new InMemoryTicketPresenceStore(_clock);

    private static TicketViewer Viewer(Guid id, string name, TicketViewerState state = TicketViewerState.Viewing) => new(id, name, state);

    [Fact]
    public void An_empty_ticket_has_no_viewers()
    {
        var presence = _store.Get(Ticket1);

        presence.TicketId.ShouldBe(Ticket1);
        presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Joining_changes_the_presence_and_joining_again_does_not()
    {
        var first = _store.Join("c1", Ticket1, Sam, "Sam");
        var again = _store.Join("c1", Ticket1, Sam, "Sam");

        first.Changed.ShouldBeTrue();
        first.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
        again.Changed.ShouldBeFalse();
        again.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
    }

    [Fact]
    public void An_agent_with_two_tabs_is_one_viewer_and_the_second_tab_changes_nothing()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");

        var second = _store.Join("tab-2", Ticket1, Sam, "Sam");

        second.Changed.ShouldBeFalse();
        second.Presence.Viewers.Count.ShouldBe(1);
    }

    [Fact]
    public void Closing_one_of_two_tabs_changes_nothing_and_closing_the_last_one_does()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");

        _store.Leave("tab-1", Ticket1).Changed.ShouldBeFalse();
        var last = _store.Leave("tab-2", Ticket1);

        last.Changed.ShouldBeTrue();
        last.Presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Viewers_are_ordered_by_name_so_equal_states_compare_equal()
    {
        _store.Join("c2", Ticket1, Sam, "Sam");
        var kim = _store.Join("c1", Ticket1, Kim, "Kim");

        kim.Presence.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);
    }

    [Fact]
    public void The_same_name_is_ordered_by_agent_id()
    {
        _store.Join("c1", Ticket1, Kim, "Alex");
        var presence = _store.Join("c2", Ticket1, Sam, "Alex");

        presence.Presence.Viewers.Select(viewer => viewer.AgentId).ShouldBe(new[] { Sam, Kim }.Order().ToList());
    }

    [Fact]
    public void Leaving_the_last_viewer_forgets_the_ticket_so_the_store_does_not_grow()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.IsEmpty.ShouldBeFalse();

        _store.Leave("c1", Ticket1);

        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Leaving_a_ticket_the_connection_never_joined_changes_nothing()
    {
        var change = _store.Leave("ghost", Ticket1);

        change.Changed.ShouldBeFalse();
        change.Presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Composing_needs_a_join_first()
    {
        _store.SetComposing("c1", Ticket1, true).ShouldBeNull();
    }

    [Fact]
    public void Composing_shows_as_replying_and_stopping_clears_it()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");

        var start = _store.SetComposing("c1", Ticket1, true)!;
        var stop = _store.SetComposing("c1", Ticket1, false)!;

        start.Changed.ShouldBeTrue();
        start.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam", TicketViewerState.Composing)]);
        stop.Changed.ShouldBeTrue();
        stop.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
    }

    [Fact]
    public void Stopping_when_not_composing_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");

        _store.SetComposing("c1", Ticket1, false)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void A_composing_hint_expires_after_the_ttl_unless_refreshed()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);

        _clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Viewing);
    }

    [Fact]
    public void A_refresh_extends_the_lease_and_is_a_change_because_it_is_the_heartbeat_the_clients_clear_on()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(TimeSpan.FromSeconds(4));

        var refresh = _store.SetComposing("c1", Ticket1, true)!;
        _clock.Advance(TimeSpan.FromSeconds(8));

        refresh.Changed.ShouldBeTrue();
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);
    }

    [Fact]
    public void A_repeat_at_the_same_instant_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);

        _store.SetComposing("c1", Ticket1, true)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Composing_again_after_the_lease_lapsed_is_a_change_because_peers_saw_viewing_meanwhile()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        _store.SetComposing("c1", Ticket1, true)!.Changed.ShouldBeTrue();
    }

    [Fact]
    public void Stopping_after_the_lease_lapsed_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        _store.SetComposing("c1", Ticket1, false)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void One_tab_composing_makes_the_agent_replying_until_that_tab_stops()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");
        _store.SetComposing("tab-1", Ticket1, true);

        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);
        _store.SetComposing("tab-1", Ticket1, false);
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Viewing);
    }

    [Fact]
    public void Leaving_everything_returns_one_entry_per_ticket_and_removes_only_that_connection()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.Join("c1", Ticket2, Sam, "Sam");
        _store.Join("c2", Ticket1, Kim, "Kim");

        var changes = _store.LeaveAll("c1");

        changes.Select(change => change.Presence.TicketId).ShouldBe([Ticket1, Ticket2], ignoreOrder: true);
        changes.ShouldAllBe(change => change.Changed);
        _store.Get(Ticket1).Viewers.ShouldBe([Viewer(Kim, "Kim")]);
        _store.Get(Ticket2).Viewers.ShouldBeEmpty();
        _store.LeaveAll("c2");
        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Leaving_everything_for_an_unknown_connection_returns_nothing()
    {
        _store.LeaveAll("ghost").ShouldBeEmpty();
    }

    [Fact]
    public void Leaving_everything_reports_no_change_for_a_ticket_the_same_agent_still_has_open_in_another_tab()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");

        var changes = _store.LeaveAll("tab-1");

        changes.Single().Changed.ShouldBeFalse();
        _store.Get(Ticket1).Viewers.Count.ShouldBe(1);
    }

    [Fact]
    public void A_connection_that_left_can_no_longer_compose_on_that_ticket()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.LeaveAll("c1");

        _store.SetComposing("c1", Ticket1, true).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrent_joins_composing_and_leaves_keep_the_store_consistent()
    {
        const int Agents = 16;
        const int Rounds = 200;
        var ids = Enumerable.Range(0, Agents).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(Enumerable.Range(0, Agents).Select(index => Task.Run(() =>
        {
            for (var round = 0; round < Rounds; round++)
            {
                var connection = $"c{index}";
                _store.Join(connection, Ticket1, ids[index], $"Agent {index:00}");
                _store.Join(connection, Ticket2, ids[index], $"Agent {index:00}");
                _store.SetComposing(connection, Ticket1, true);
                _store.Get(Ticket1);
                _store.Leave(connection, Ticket2);
                _store.SetComposing(connection, Ticket1, false);
            }
        }, TestContext.Current.CancellationToken)));

        // Every agent is still on ticket 1 (never left it) and nobody is on ticket 2 (every join was followed by a leave).
        _store.Get(Ticket1).Viewers.Count.ShouldBe(Agents);
        _store.Get(Ticket1).Viewers.ShouldAllBe(viewer => viewer.State == TicketViewerState.Viewing);
        _store.Get(Ticket2).Viewers.ShouldBeEmpty();

        await Task.WhenAll(Enumerable.Range(0, Agents).Select(index => Task.Run(() => _store.LeaveAll($"c{index}"), TestContext.Current.CancellationToken)));

        _store.Get(Ticket1).Viewers.ShouldBeEmpty();
        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_viewer_snapshot_is_a_copy_that_later_changes_do_not_touch()
    {
        var before = _store.Join("c1", Ticket1, Sam, "Sam").Presence;

        _store.Join("c2", Ticket1, Kim, "Kim");

        before.Viewers.Count.ShouldBe(1);
    }
}
