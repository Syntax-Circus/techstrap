using System.Diagnostics.Metrics;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// TechStrap's own instruments (the first custom meter, D-046): how many distinct agents are connected to the hub, how many ticket changes the listener relayed, how many relays
/// failed (a bad payload counts) and how many times the listener got its database connection back. The names are constants because dashboards and alerts depend on them.
/// The meter only reaches an exporter when its name is passed to <c>AddSyntaxCircusObservability</c> (the Api and the Worker do), and then only when OpenTelemetry metrics are on.
/// Handlers never see this class (they may depend on Application abstractions only): the hub and the listener, which are adapters, record.
/// </summary>
public sealed class TechStrapMetrics : IDisposable
{
    public const string MeterName = "TechStrap";
    public const string ConnectedAgentsName = "techstrap.live.connected_agents";
    public const string ChangesRelayedName = "techstrap.live.changes_relayed";
    public const string RelayFailuresName = "techstrap.live.relay_failures";
    public const string ListenerReconnectsName = "techstrap.live.listener_reconnects";

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _subjectByConnection = [];
    private readonly Counter<long> _changesRelayed;
    private readonly Counter<long> _relayFailures;
    private readonly Counter<long> _listenerReconnects;

    public TechStrapMetrics()
    {
        Meter = new Meter(MeterName);
        Meter.CreateObservableGauge(ConnectedAgentsName, () => ConnectedAgents, unit: "{agent}", description: "Distinct agents with at least one open hub connection.");
        _changesRelayed = Meter.CreateCounter<long>(ChangesRelayedName, unit: "{change}", description: "Ticket changes the listener relayed from Postgres to the hub.");
        _relayFailures = Meter.CreateCounter<long>(RelayFailuresName, unit: "{change}", description: "Notifications the listener could not relay (invalid, too large or the hub failed).");
        _listenerReconnects = Meter.CreateCounter<long>(ListenerReconnectsName, unit: "{reconnect}", description: "Times the listener got its database connection back after losing it.");
    }

    internal Meter Meter { get; }

    /// <summary>True while an exporter or listener is subscribed to the counters: how a test proves the meter name was registered.</summary>
    public bool IsObserved => _changesRelayed.Enabled;

    /// <summary>Agents, not connections: an agent with three tabs counts once.</summary>
    public int ConnectedAgents
    {
        get
        {
            lock (_gate)
            {
                return _subjectByConnection.Values.Distinct(StringComparer.Ordinal).Count();
            }
        }
    }

    public void AgentConnected(string connectionId, string subject)
    {
        lock (_gate)
        {
            _subjectByConnection[connectionId] = subject;
        }
    }

    public void AgentDisconnected(string connectionId)
    {
        lock (_gate)
        {
            _subjectByConnection.Remove(connectionId);
        }
    }

    public void ChangeRelayed() => _changesRelayed.Add(1);

    public void RelayFailed() => _relayFailures.Add(1);

    public void ListenerReconnected() => _listenerReconnects.Add(1);

    public void Dispose() => Meter.Dispose();
}
