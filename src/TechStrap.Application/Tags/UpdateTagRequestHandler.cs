using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tags;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Tags;

public interface IUpdateTagRequestHandler
{
    Task<Result<TagDto>> HandleAsync(Guid tagId, UpdateTagRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/tags/{id} (Admin, D-022). Changes the name and colour; the slug is permanent. An unchanged update is not audited.</summary>
public sealed class UpdateTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITagRepository tags,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateTagRequestHandler
{
    public async Task<Result<TagDto>> HandleAsync(Guid tagId, UpdateTagRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<TagDto>.Failure(actor.Errors[0]);
        }

        var tag = await tags.GetByIdAsync(tagId, cancellationToken);
        if (tag is null)
        {
            return Result<TagDto>.Failure(TagErrors.NotFound());
        }

        var changed = new List<string>();
        if (!string.Equals(tag.Name, request.Name?.Trim(), StringComparison.Ordinal))
        {
            changed.Add("name");
        }

        if (!string.Equals(tag.Colour, request.Colour?.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            changed.Add("colour");
        }

        var updated = tag.Update(request.Name, request.Colour);
        if (updated.IsFailure)
        {
            return Result<TagDto>.Failure(updated.Error!.ToError());
        }

        if (changed.Count == 0)
        {
            return Result<TagDto>.Success(TagMapping.ToDto(tag));
        }

        tags.Update(tag);
        AdminAudit.Record(adminEvents, AdminEventType.TagUpdated, actor.Value, AdminSubjectType.Tag, tag.Id, new { slug = tag.Slug, changed }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsFailure ? Result<TagDto>.Failure(committed.Errors[0]) : Result<TagDto>.Success(TagMapping.ToDto(tag));
    }
}
