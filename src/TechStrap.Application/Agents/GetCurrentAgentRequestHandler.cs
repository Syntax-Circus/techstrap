using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Agents;
using TechStrap.Domain;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Agents;

public interface IGetCurrentAgentRequestHandler
{
    Task<Result<AgentDto>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/agents/me (D-004, D-029). The first call provisions the agent; every call refreshes name and email from the token,
/// mirrors the group-derived role and records the sign-in time. Two first calls racing each other create one row: the loser's
/// insert fails as a duplicate and it retries once, finding the winner's row.
/// </summary>
public sealed class GetCurrentAgentRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IGetCurrentAgentRequestHandler
{
    private const int MaxAttempts = 2;

    public async Task<Result<AgentDto>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<AgentDto>.Failure(AgentErrors.AccessRequired());
        }

        if (string.IsNullOrWhiteSpace(claims.Email))
        {
            return Result<AgentDto>.Failure(AgentErrors.EmailRequired());
        }

        var name = Shorten(claims.Name);
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = await unitOfWork.BeginAsync(cancellationToken);
            var agent = await agents.GetBySubjectAsync(claims.Subject, cancellationToken);
            if (agent is null)
            {
                var created = Agent.Create(claims.Subject, name, claims.Email, claims.Role, clock);
                if (created.IsFailure)
                {
                    return Result<AgentDto>.Failure(AgentErrors.IdentityInvalid(created.Error!.Message));
                }

                agent = created.Value;
                agent.RecordSeen(clock);
                agents.Add(agent);
            }
            else
            {
                if (!agent.IsActive)
                {
                    return Result<AgentDto>.Failure(AgentErrors.Inactive());
                }

                var refreshed = agent.UpdateIdentity(name, claims.Email);
                if (refreshed.IsFailure)
                {
                    return Result<AgentDto>.Failure(AgentErrors.IdentityInvalid(refreshed.Error!.Message));
                }

                agent.ChangeRole(claims.Role);
                agent.RecordSeen(clock);
                agents.Update(agent);
            }

            var committed = await scope.CommitAsync(cancellationToken);
            if (committed.IsSuccess)
            {
                return Result<AgentDto>.Success(AgentMapping.ToDto(agent));
            }

            if (attempt < MaxAttempts && committed.Errors[0].Code == PersistenceErrorCodes.Duplicate)
            {
                continue;
            }

            return Result<AgentDto>.Failure(committed.Errors[0]);
        }
    }

    private static string? Shorten(string? name)
    {
        var text = name?.Trim();
        return text is { Length: > DomainLimits.NameMaxLength } ? text[..DomainLimits.NameMaxLength] : text;
    }
}
