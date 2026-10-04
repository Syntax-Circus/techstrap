using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Auth;

public enum AgentSessionState
{
    /// <summary><c>GET /api/agents/me</c> has not answered yet.</summary>
    NotLoaded,

    /// <summary>The API knows the agent and they may work: <see cref="AgentSession.Agent"/> is set.</summary>
    Ready,

    /// <summary>The API refused the agent (no agent group, deactivated, no email, unusable identity): show the no-access page and make no other call.</summary>
    NoAccess,

    /// <summary>The API rejected the token (401). The agent signs in again.</summary>
    SessionExpired,

    /// <summary>The API could not be reached or answered something unexpected. Retryable.</summary>
    Unavailable,
}

/// <summary>
/// Who the signed-in user is, according to the API (D-040). The Admin does not parse group claims: the first call of a circuit is <c>GET /api/agents/me</c>,
/// which also creates the agent row that every ticket call needs, and its answer decides everything. Components that show ticket data sit inside
/// <c>AgentGate</c>, which renders them only when <see cref="State"/> is <see cref="AgentSessionState.Ready"/>, so NoAccess always wins and no ticket call
/// precedes a successful <c>/me</c>. Scoped: one per circuit (and one per prerender request).
/// </summary>
public sealed class AgentSession(IAgentsClient agents)
{
    private Task? _loading;

    public AgentSessionState State { get; private set; }

    /// <summary>The signed-in agent when <see cref="State"/> is Ready.</summary>
    public AgentDto? Agent { get; private set; }

    /// <summary>The API's error code when the state is NoAccess, SessionExpired or Unavailable (for example agent-inactive).</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The API's message for that error, written for the agent to read.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Admin-only actions (delete, erase) show only for an Admin. The API still enforces it.</summary>
    public bool IsAdmin => State == AgentSessionState.Ready && Agent?.Role == AgentRoles.Admin;

    /// <summary>Raised after every state change so the gate and the navigation can re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the session once. Concurrent callers share one request. Ready and NoAccess are final for the life of the scope (the stored role is refreshed
    /// by the API on every <c>/me</c>, so a deliberate <see cref="ReloadAsync"/> picks up a change); SessionExpired and Unavailable are tried again on the next call.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (State is AgentSessionState.Ready or AgentSessionState.NoAccess)
            {
                return;
            }

            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _loading, source.Task, null) is not { } running)
            {
                await LoadAsync(source, keepReadyOnTransientFailure: false, cancellationToken);
                return;
            }

            // Someone else is already asking; share their answer. If their load ended without one (they were cancelled), ask again ourselves.
            await running;
            if (State != AgentSessionState.NotLoaded)
            {
                return;
            }
        }
    }

    private async Task LoadAsync(TaskCompletionSource source, bool keepReadyOnTransientFailure, CancellationToken cancellationToken)
    {
        try
        {
            Apply(await agents.GetMeAsync(cancellationToken), keepReadyOnTransientFailure);
        }
        finally
        {
            // A failed or cancelled load must not pin the shared task: the next call asks again unless the state is final.
            _loading = null;
            source.SetResult();
        }
    }

    /// <summary>
    /// Asks the API again (the Retry button, or after the agent changed their own profile). A session that is Ready stays Ready, with the current
    /// <see cref="Agent"/>, until the answer arrives: dropping to NotLoaded would make <c>AgentGate</c> and <c>AdminOnly</c> replace the page with "Checking" and lose
    /// what the agent was typing (the My settings save reloads the session). <see cref="Changed"/> is raised once, when the answer has been applied. The answer still
    /// wins: a 403 ends in NoAccess, a 401 in SessionExpired, a lower role removes the admin pages; only a transient failure (the API unreachable, a timeout)
    /// keeps the Ready session, because an agent who was working a moment ago is not locked out by one lost request. A session that is not Ready starts again from
    /// NotLoaded, so the gate shows "Checking" while the Retry button's request is in flight.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var keepReady = State == AgentSessionState.Ready;
        if (!keepReady)
        {
            State = AgentSessionState.NotLoaded;
        }

        while (true)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _loading, source.Task, null) is not { } running)
            {
                await LoadAsync(source, keepReady, cancellationToken);
                return;
            }

            // Another load is in flight; its answer may be older than the change that prompted this reload, so wait for it and then ask again.
            await running;
        }
    }

    private void Apply(Result<AgentDto> result, bool keepReadyOnTransientFailure)
    {
        if (keepReadyOnTransientFailure && result.IsFailure && result.Errors[0].Kind is not (ResultErrorKind.Forbidden or ResultErrorKind.Unauthenticated))
        {
            return;
        }

        if (result.IsSuccess && result.Value.IsActive)
        {
            Agent = result.Value;
            State = AgentSessionState.Ready;
            ErrorCode = ErrorMessage = null;
        }
        else
        {
            Agent = null;
            var error = result.IsSuccess
                ? new ResultError(ApiErrorCodes.AgentInactive, "Your agent account is deactivated.", ResultErrorKind.Forbidden)
                : result.Errors[0];
            ErrorCode = error.Code;
            ErrorMessage = error.Message;
            State = error.Kind switch
            {
                ResultErrorKind.Forbidden => AgentSessionState.NoAccess,
                ResultErrorKind.Unauthenticated => AgentSessionState.SessionExpired,
                _ => AgentSessionState.Unavailable,
            };
        }

        Changed?.Invoke();
    }
}
