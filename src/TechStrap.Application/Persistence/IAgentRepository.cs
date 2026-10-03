using SyntaxCircus.Common;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Persistence;

public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Agent?> GetBySubjectAsync(string oidcSubject, CancellationToken cancellationToken);

    /// <summary>
    /// Ordered by name then email. Implementations normalize <paramref name="page"/> and <paramref name="pageSize"/> through
    /// <see cref="Paging"/> before querying.
    /// </summary>
    Task<PagedResult<Agent>> ListAsync(bool activeOnly, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The agents with these ids; unknown ids are skipped.</summary>
    Task<IReadOnlyList<Agent>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Counts active admins and locks their rows until the current unit of work ends, so two concurrent deactivations cannot both
    /// see "another admin is still active" (D-029). Must run inside an IUnitOfWork scope.
    /// </summary>
    Task<int> CountActiveAdminsLockedAsync(CancellationToken cancellationToken);

    void Add(Agent agent);

    void Update(Agent agent);

    Task<IReadOnlyList<AgentNotificationPreference>> ListNotificationPreferencesAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>Inserts or updates the (agent, product) preference. Staged, written on commit.</summary>
    Task SetNotificationPreferenceAsync(AgentNotificationPreference preference, CancellationToken cancellationToken);

    /// <summary>Active agents who opted in to new-ticket alerts for the product.</summary>
    Task<IReadOnlyList<Agent>> ListAgentsToAlertForProductAsync(Guid productId, CancellationToken cancellationToken);
}
