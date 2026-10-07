namespace TechStrap.Infrastructure.Live;

/// <summary>The Postgres NOTIFY relay between the Worker and the Api (D-007, D-018): the channel and the listener's timings. Constants, not settings: nothing here is for an operator to tune.</summary>
public static class TicketChangeNotify
{
    /// <summary>The channel the Worker notifies and the Api listens on.</summary>
    public const string Channel = "techstrap_ticket_changes";

    /// <summary>The listener connection's name in <c>pg_stat_activity</c>, so an operator can see it (and a test can find it).</summary>
    public const string ListenerApplicationName = "techstrap-ticket-change-listener";

    /// <summary>Seconds of silence before Npgsql sends a keepalive on the listener's connection, so an idle NAT or firewall does not drop it.</summary>
    public const int KeepAliveSeconds = 30;

    /// <summary>The wait before the first reconnect; each further failure in a row doubles it.</summary>
    public static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between reconnect attempts.</summary>
    public static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);
}
