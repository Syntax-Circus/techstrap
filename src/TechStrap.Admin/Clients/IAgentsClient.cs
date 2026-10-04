using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>The signed-in agent and the agent list (assignee picker). Components never see HTTP: every failure is a <see cref="Result"/> error.</summary>
public interface IAgentsClient
{
    /// <summary>
    /// <c>GET /api/agents/me</c>. The first call after sign-in also creates the agent row, so no ticket call works before it. 403 types:
    /// agent-access-required, agent-inactive, agent-email-required, agent-identity-invalid.
    /// </summary>
    Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>Every agent the caller may see (active agents for an Agent, everyone for an Admin), fetched page by page at the API maximum of 100.</summary>
    Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/agents?page=&amp;pageSize=</c>: one page for the Agents screen. The API clamps both numbers (the page size to 100), so a 400 is not expected.</summary>
    Task<Result<PagedResponse<AgentListItemDto>>> ListPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/agents/{id}</c> (Admin): activates or deactivates an agent and returns the agent. Never retried. A 409 <c>last-active-admin</c> means the
    /// change would leave no active admin; a 404 <c>agent-not-found</c> means the agent is gone. Roles cannot be changed here (D-029, D-041).
    /// </summary>
    Task<Result<AgentDto>> SetActiveAsync(Guid agentId, bool isActive, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/agents/me/profile</c> (204): sets, or with a blank name clears, the customer-facing display name. 400 fields: public-display-name (too long, or contains @).</summary>
    Task<Result> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/agents/me/notification-preferences</c>: one row per active product, in the API's order.</summary>
    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferencesAsync(CancellationToken cancellationToken);

    /// <summary><c>PUT /api/agents/me/notification-preferences</c> (204): the full set of per-product choices. Idempotent.</summary>
    Task<Result> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken);
}
