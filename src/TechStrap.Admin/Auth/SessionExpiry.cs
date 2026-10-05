namespace TechStrap.Admin.Auth;

/// <summary>
/// The single choke point for "the API answered 401 to this agent" (PHASE-07c). <c>ApiConnection</c>, the only place the Admin talks HTTP to the API, calls
/// <see cref="Report"/> for every 401, and <see cref="AgentSession"/> listens, so no page needs its own code and none can forget it. It is a separate scoped
/// object (not a call from the connection to the session) because the session depends on the agents client, which depends on the connection.
/// </summary>
public sealed class SessionExpiry
{
    /// <summary>Raised on every reported 401. It may be raised from any thread.</summary>
    public event Action? Lapsed;

    public void Report() => Lapsed?.Invoke();
}
