namespace TechStrap.Contracts.Live;

/// <summary>
/// A presence call from the hub. The hub fills it from its own caller (<c>Context.User</c> and <c>Context.ConnectionId</c>), never from client input,
/// so a client cannot speak for another agent. <paramref name="TicketId"/> is null for <c>LeaveAll</c>.
/// </summary>
public sealed record UpdateTicketPresenceRequest(string Action, string AgentSubject, string ConnectionId, Guid? TicketId, bool IsComposing);

/// <summary>A payload the Postgres listener received, exactly as text: the handler parses and validates it, because anything could write to the channel.</summary>
public sealed record RelayTicketChangeRequest(string Payload);
