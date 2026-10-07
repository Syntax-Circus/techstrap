namespace TechStrap.Contracts.Live;

/// <summary>Where the agent hub is mapped. The Admin builds its hub address from the API base address and this path.</summary>
public static class TicketHubRoutes
{
    public const string Path = "/hubs/tickets";
}

/// <summary>
/// Hub method names (D-018, D-046). The first three are invoked by the client, the last two are pushed by the server. They cross
/// a process boundary, so they are constants and never typed twice.
/// </summary>
public static class TicketHubMethods
{
    public const string JoinTicket = "JoinTicket";
    public const string LeaveTicket = "LeaveTicket";
    public const string SetComposing = "SetComposing";
    public const string TicketChanged = "TicketChanged";
    public const string PresenceChanged = "PresenceChanged";
}

/// <summary>
/// Hub group names. Every connection is in <see cref="Queue"/> and receives every <c>TicketChanged</c> (clients filter by ticket id, D-046);
/// <see cref="Ticket"/> groups carry presence only.
/// </summary>
public static class TicketHubGroups
{
    public const string Queue = "queue";

    private const string TicketPrefix = "ticket:";

    public static string Ticket(Guid ticketId) => TicketPrefix + ticketId.ToString("D");
}

/// <summary>Wire names for the kind of a ticket change. Contracts carries no enums (naming rule). <c>Resync</c> names no ticket: the client reloads everything.</summary>
public static class TicketChangeKinds
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Resync = "Resync";
}

/// <summary>Wire names for what an agent is doing on a ticket page. Contracts carries no enums (naming rule).</summary>
public static class TicketPresenceStates
{
    public const string Viewing = "Viewing";
    public const string Composing = "Composing";
}

/// <summary>The presence actions a hub call asks <c>UpdateTicketPresenceHandler</c> for.</summary>
public static class TicketPresenceActions
{
    public const string Join = "Join";
    public const string Leave = "Leave";
    public const string LeaveAll = "LeaveAll";
    public const string SetComposing = "SetComposing";
}

/// <summary>Limits shared by the server and the Admin client.</summary>
public static class TicketLiveLimits
{
    /// <summary>A composing hint that is not refreshed within this many seconds counts as viewing again.</summary>
    public const int ComposingTtlSeconds = 10;

    /// <summary>
    /// The largest change payload (UTF-8 bytes) the relay accepts. A real payload is about 300 bytes; Postgres allows 8,000 bytes in a NOTIFY, so
    /// the cap is a safety margin against a stray writer, not a design limit.
    /// </summary>
    public const int MaxChangePayloadBytes = 2048;
}
