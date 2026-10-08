namespace TechStrap.Contracts.Live;

/// <summary>Where the agent hub is mapped. The Admin builds its hub address from the API base address and this path.</summary>
public static class TicketHubRoutes
{
    /// <summary>The hub path, <c>/hubs/tickets</c>, relative to the API base address.</summary>
    public const string Path = "/hubs/tickets";
}

/// <summary>
/// Hub method names (D-018, D-046). The first three are invoked by the client, the last two are pushed by the server. They cross
/// a process boundary, so they are constants and never typed twice.
/// </summary>
public static class TicketHubMethods
{
    /// <summary>Client to server: the caller opens a ticket page and joins its presence group. Argument: the ticket id; returns the current presence of the ticket.</summary>
    public const string JoinTicket = "JoinTicket";
    /// <summary>Client to server: the caller leaves a ticket's presence group. Argument: the ticket id.</summary>
    public const string LeaveTicket = "LeaveTicket";
    /// <summary>Client to server: the caller starts or stops typing a reply on a ticket. Arguments: the ticket id and whether the caller is composing.</summary>
    public const string SetComposing = "SetComposing";
    /// <summary>Server to client: a ticket was created or updated, or the client must resync. Delivered to every connected agent.</summary>
    public const string TicketChanged = "TicketChanged";
    /// <summary>Server to client: who is viewing or composing on a ticket changed. Delivered to that ticket's presence group.</summary>
    public const string PresenceChanged = "PresenceChanged";
}

/// <summary>
/// Hub group names. Every connection is in <see cref="Queue"/> and receives every <c>TicketChanged</c> (clients filter by ticket id, D-046);
/// <see cref="Ticket"/> groups carry presence only.
/// </summary>
public static class TicketHubGroups
{
    /// <summary>The group every connection joins; it receives every <c>TicketChanged</c>. Value <c>queue</c>.</summary>
    public const string Queue = "queue";

    private const string TicketPrefix = "ticket:";

    /// <summary>The presence group name for one ticket, <c>ticket:{id}</c> with the id in <c>D</c> format.</summary>
    /// <param name="ticketId">The ticket whose presence group is named.</param>
    public static string Ticket(Guid ticketId) => TicketPrefix + ticketId.ToString("D");
}

/// <summary>The one message a hub call sends to a client when it names a ticket that does not exist (D-046). Every other refusal sends its own fixed handler text.</summary>
public static class TicketHubMessages
{
    /// <summary>The text sent when a hub call names a ticket that does not exist: <c>Ticket not found</c>.</summary>
    public const string TicketNotFound = "Ticket not found";
}

/// <summary>Wire names for the kind of a ticket change. Contracts carries no enums (naming rule). <c>Resync</c> names no ticket: the client reloads everything.</summary>
public static class TicketChangeKinds
{
    /// <summary>A new ticket appeared.</summary>
    public const string Created = "Created";
    /// <summary>An existing ticket changed (a message, status, assignment, tag and so on).</summary>
    public const string Updated = "Updated";
    /// <summary>The client missed changes and must reload everything; it names no ticket.</summary>
    public const string Resync = "Resync";
}

/// <summary>Wire names for what an agent is doing on a ticket page. Contracts carries no enums (naming rule).</summary>
public static class TicketPresenceStates
{
    /// <summary>The agent has the ticket page open.</summary>
    public const string Viewing = "Viewing";
    /// <summary>The agent is typing a reply on the ticket.</summary>
    public const string Composing = "Composing";
}

/// <summary>The presence actions a hub call asks <c>UpdateTicketPresenceHandler</c> for.</summary>
public static class TicketPresenceActions
{
    /// <summary>The agent opened the ticket page.</summary>
    public const string Join = "Join";
    /// <summary>The agent left one ticket page.</summary>
    public const string Leave = "Leave";
    /// <summary>The agent's connection dropped, so it leaves every ticket.</summary>
    public const string LeaveAll = "LeaveAll";
    /// <summary>The agent started or stopped typing a reply.</summary>
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
