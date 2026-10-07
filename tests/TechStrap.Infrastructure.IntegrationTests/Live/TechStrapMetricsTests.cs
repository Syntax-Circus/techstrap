using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>The instruments are real: a <see cref="System.Diagnostics.Metrics.MeterListener"/> sees them by their constant names and they move when the adapters record.</summary>
public sealed class TechStrapMetricsTests
{
    [Fact]
    public void The_names_are_pinned_constants()
    {
        TechStrapMetrics.MeterName.ShouldBe("TechStrap");
        TechStrapMetrics.ConnectedAgentsName.ShouldBe("techstrap.live.connected_agents");
        TechStrapMetrics.ChangesRelayedName.ShouldBe("techstrap.live.changes_relayed");
        TechStrapMetrics.RelayFailuresName.ShouldBe("techstrap.live.relay_failures");
        TechStrapMetrics.ListenerReconnectsName.ShouldBe("techstrap.live.listener_reconnects");
    }

    [Fact]
    public void A_listener_sees_the_four_instruments_with_their_kinds()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);

        probe.Published.ShouldBe(
            [
                $"{TechStrapMetrics.ConnectedAgentsName}:ObservableGauge`1",
                $"{TechStrapMetrics.ChangesRelayedName}:Counter`1",
                $"{TechStrapMetrics.RelayFailuresName}:Counter`1",
                $"{TechStrapMetrics.ListenerReconnectsName}:Counter`1",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_gauge_counts_distinct_agents_not_connections()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(0);

        metrics.AgentConnected("c1", "sam");
        metrics.AgentConnected("c2", "sam");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(1);

        metrics.AgentConnected("c3", "kim");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(2);

        metrics.AgentDisconnected("c1");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(2);

        metrics.AgentDisconnected("c2");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(1);

        metrics.AgentDisconnected("c2");
        metrics.AgentDisconnected("unknown");
        metrics.AgentDisconnected("c3");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(0);
    }

    [Fact]
    public void Connecting_the_same_connection_twice_does_not_count_twice()
    {
        using var metrics = new TechStrapMetrics();

        metrics.AgentConnected("c1", "sam");
        metrics.AgentConnected("c1", "sam");

        metrics.ConnectedAgents.ShouldBe(1);
    }

    [Fact]
    public void Each_counter_moves_by_one_per_call_and_independently()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);

        metrics.ChangeRelayed();
        metrics.ChangeRelayed();
        metrics.RelayFailed();
        metrics.ListenerReconnected();
        metrics.ListenerReconnected();
        metrics.ListenerReconnected();

        probe.Sum(TechStrapMetrics.ChangesRelayedName).ShouldBe(2);
        probe.Sum(TechStrapMetrics.RelayFailuresName).ShouldBe(1);
        probe.Sum(TechStrapMetrics.ListenerReconnectsName).ShouldBe(3);
    }

    [Fact]
    public void A_probe_on_one_instance_does_not_see_another_instance()
    {
        using var first = new TechStrapMetrics();
        using var second = new TechStrapMetrics();
        using var probe = new MetricsProbe(first);

        second.ChangeRelayed();
        first.ChangeRelayed();

        probe.Sum(TechStrapMetrics.ChangesRelayedName).ShouldBe(1);
    }

    [Fact]
    public void The_meter_is_observed_only_while_something_listens()
    {
        using var metrics = new TechStrapMetrics();
        metrics.IsObserved.ShouldBeFalse();

        using var probe = new MetricsProbe(metrics);

        metrics.IsObserved.ShouldBeTrue();
    }
}
