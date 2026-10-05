using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUploadKbImageRequestHandler
{
    Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/images (Agent). The signed-in agent must be active, checked before anything is stored. Hands the bytes to <see cref="IKbImageStore"/>, which checks the size and the type and stores them under a random key,
/// then returns that key and the absolute public URL to put in the Markdown. No database row is written and unused images are not cleaned up (D-044).
/// </summary>
public sealed class UploadKbImageRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IKbImageStore store,
    IKbImageUrls urls) : IUploadKbImageRequestHandler
{
    public async Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbImageUploadResponse>.Failure(actor.Errors[0]);
        }

        var stored = await store.SaveAsync(image, cancellationToken);
        return stored.IsFailure
            ? Result<KbImageUploadResponse>.Failure(stored.Errors[0])
            : Result<KbImageUploadResponse>.Success(new KbImageUploadResponse(stored.Value.Key, urls.UrlFor(stored.Value.FileName)));
    }
}
