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

    /// <summary>The copy for the session-expired failure, shared by the session and by <c>ApiConnection</c>'s local refusal.</summary>
    public const string ExpiredMessage = "Your session has expired. Sign in again.";

    /// <summary>
    /// True once any call has been answered 401. It never goes back: the only way to a working session is a new sign-in, which is a new circuit and so a new
    /// instance. <c>ApiConnection</c> sends nothing while this is true (the token is already evicted, so a request could only go out unauthenticated and its path and
    /// query, which can carry what the agent searched for, would be logged).
    /// </summary>
    public bool IsLapsed { get; private set; }

    public void Report()
    {
        IsLapsed = true;
        Lapsed?.Invoke();
    }
}
