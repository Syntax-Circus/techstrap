using SyntaxCircus.Common;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateAgentRequestHandler
{
    Task<Result<AgentDto>> HandleAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/agents/{id} (Admin, D-022). Activates or deactivates an agent; roles come from IdP groups and cannot change here (D-029).
/// The last active admin cannot be deactivated: the active admin rows are locked first, so concurrent deactivations queue.
/// </summary>
public sealed class UpdateAgentRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateAgentRequestHandler
{
    public async Task<Result<AgentDto>> HandleAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<AgentDto>.Failure(actor.Errors[0]);
        }

        if (request.IsActive is not { } isActive)
        {
            return Result<AgentDto>.Failure(new ResultError("is-active-required", "Send isActive as true or false.", ResultErrorKind.Validation, "isActive"));
        }

        var activeAdmins = isActive ? 0 : await agents.CountActiveAdminsLockedAsync(cancellationToken);
        var agent = await agents.GetByIdAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return Result<AgentDto>.Failure(AgentErrors.NotFound());
        }

        if (agent.IsActive == isActive)
        {
            return Result<AgentDto>.Success(AgentMapping.ToDto(agent));
        }

        if (!isActive && agent.Role == AgentRole.Admin && activeAdmins <= 1)
        {
            return Result<AgentDto>.Failure(AgentErrors.LastActiveAdmin());
        }

        agent.SetActive(isActive);
        agents.Update(agent);
        AdminAudit.Record(adminEvents, AdminEventType.AgentUpdated, actor.Value, AdminSubjectType.Agent, agent.Id, new { isActive }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsSuccess ? Result<AgentDto>.Success(AgentMapping.ToDto(agent)) : Result<AgentDto>.Failure(committed.Errors[0]);
    }
}
