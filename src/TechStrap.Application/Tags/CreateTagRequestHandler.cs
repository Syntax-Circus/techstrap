using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tags;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tags;

public interface ICreateTagRequestHandler
{
    Task<Result<TagDto>> HandleAsync(CreateTagRequest request, CancellationToken cancellationToken);
}

/// <summary>POST /api/tags (Admin, D-022). The slug is permanent; a slug that is already taken is a 409.</summary>
public sealed class CreateTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITagRepository tags,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateTagRequestHandler
{
    public async Task<Result<TagDto>> HandleAsync(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var created = Tag.Create(request.Slug, request.Name, request.Colour, clock);
        if (created.IsFailure)
        {
            return Result<TagDto>.Failure(created.Error!.ToError());
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<TagDto>.Failure(actor.Errors[0]);
        }

        var tag = created.Value;
        tags.Add(tag);
        AdminAudit.Record(adminEvents, AdminEventType.TagCreated, actor.Value, AdminSubjectType.Tag, tag.Id, new { slug = tag.Slug }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<TagDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? TagErrors.SlugTaken() : committed.Errors[0]);
        }

        return Result<TagDto>.Success(TagMapping.ToDto(tag));
    }
}
