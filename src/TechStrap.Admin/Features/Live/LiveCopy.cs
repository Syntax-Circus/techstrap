namespace TechStrap.Admin.Features.Live;

/// <summary>The words of the live-update screens. Plain text; the en dash is written as an escape so no source file holds a raw non-ASCII character.</summary>
public static class LiveCopy
{
    /// <summary>Said before the state by a screen reader (hidden on screen): "Live updates: Reconnecting".</summary>
    public const string Label = "Live updates:";

    public const string Connected = "Live";
    public const string Connecting = "Connecting";
    public const string Reconnecting = "Reconnecting";
    public const string Disconnected = "Offline";

    /// <summary>The queue banner: something changed somewhere; nothing has moved yet.</summary>
    public const string QueueUpdated = "Queue updated \u2013 refresh";

    /// <summary>The ticket banner: another agent or the customer did something here; the page shows the old version until it is clicked.</summary>
    public const string NewActivity = "New activity \u2013 refresh";

    public static string For(LiveConnectionState state) => state switch
    {
        LiveConnectionState.Connected => Connected,
        LiveConnectionState.Connecting => Connecting,
        LiveConnectionState.Reconnecting => Reconnecting,
        _ => Disconnected,
    };
}

/// <summary>The live screens' named constants (a literal number never sits at a call site).</summary>
public static class LiveDefaults
{
    /// <summary>
    /// The queue banner waits this long after the first change before it appears, and every change in the window joins it: a burst raises one banner, and a steady trickle still shows it within this time
    /// (a window that restarted on every change would never fire).
    /// </summary>
    public static readonly TimeSpan QueueBannerWindow = TimeSpan.FromSeconds(1);

    /// <summary>The composer sends "I am typing" at most this often while the agent types. The server's lease is 10 seconds, so a refresh every 4 keeps "replying" alive.</summary>
    public static readonly TimeSpan ComposingThrottle = TimeSpan.FromSeconds(4);
}
