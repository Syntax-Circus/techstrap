using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateMyProfileRequestHandler
{
    Task<Result> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/agents/me/profile: the caller sets or clears their customer-facing display name (D-024).</summary>
public sealed class UpdateMyProfileRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents, IUnitOfWork unitOfWork) : IUpdateMyProfileRequestHandler
{
    public async Task<Result> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure(me.Errors[0]);
        }

        var changed = me.Value.SetPublicDisplayName(request.PublicDisplayName);
        if (changed.IsFailure)
        {
            return changed.ToResult();
        }

        agents.Update(me.Value);
        return await scope.CommitAsync(cancellationToken);
    }
}
