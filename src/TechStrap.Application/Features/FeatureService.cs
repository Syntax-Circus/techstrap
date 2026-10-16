using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Content;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Features;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Features;

/// <summary>Shared orchestration for the feature board request handlers.</summary>
public interface IFeatureService
{
    Task<Result<FeatureDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<PagedResponse<FeatureSummaryDto>>> ListAsync(Guid? productId, string? q, string? status, int page, int pageSize, CancellationToken ct);
    Task<Result<FeatureDto>> CreateAsync(CreateFeatureRequest request, CancellationToken ct);
    Task<Result<FeatureDto>> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken ct);
    Task<Result<FeatureDto>> SetPublishedAsync(Guid id, bool published, uint version, CancellationToken ct);
    Task<Result<PagedResponse<PublicFeatureSummaryDto>>> ListPublicAsync(string key, string? q, string? status, int page, int pageSize, CancellationToken ct);
    Task<Result<PublicFeatureDto>> GetPublicAsync(string key, string slug, CancellationToken ct);
    Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> ListTicketsAsync(Guid id, CancellationToken ct);
}

public sealed class FeatureService(
    ICurrentAgentClaims claims,
    IAgentRepository agents,
    IProductRepository products,
    IFeatureRepository features,
    IKbContentRenderer renderer,
    IAdminEventRepository events,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IFeatureService
{
    private static ResultError Missing() => new("feature-not-found", "That feature does not exist.", ResultErrorKind.NotFound);

    private static ResultError InvalidStatus() => new("status-invalid", "Choose a supported feature progress status.", ResultErrorKind.Validation, "status");

    private static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "This feature changed. Reload it before saving.", ResultErrorKind.Conflict);

    private static ResultError SlugTaken() => new("feature-slug-taken", "Another feature in this product uses that slug.", ResultErrorKind.Conflict);

    private static FeatureDto Dto(
        Feature f) =>
        new(f.Id, f.ProductId, f.Slug, f.Title, f.DescriptionMarkdown, f.PublicationState.ToString(), f.ProgressStatus.ToString(), f.CreatedAt, f.UpdatedAt, f.Version);

    private static bool Parse(string? value, out FeatureProgressStatus? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (!Enum.TryParse<FeatureProgressStatus>(value, false, out var s) || !Enum.IsDefined(s) || s.ToString() != value)
        {
            return false;
        }
        parsed = s;
        return true;
    }

    public async Task<Result<FeatureDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var f = await features.GetAsync(id, ct);
        return f is null ? Result<FeatureDto>.Failure(Missing()) : Result<FeatureDto>.Success(Dto(f));
    }

    public async Task<Result<PagedResponse<FeatureSummaryDto>>> ListAsync(
        Guid? productId,
        string? q,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (!Parse(status, out var s))
        {
            return Result<PagedResponse<FeatureSummaryDto>>.Failure(InvalidStatus());
        }
        var r = await features.ListAsync(productId, false, s, q, page, pageSize, ct);
        return Result<PagedResponse<FeatureSummaryDto>>.Success(new(r.Items.Select(f => new FeatureSummaryDto(f.Id, f.ProductId, f.Slug, f.Title, f.PublicationState.ToString(), f.ProgressStatus.ToString(), f.UpdatedAt)).ToArray(), r.Page, r.PageSize, r.TotalCount));
    }

    public async Task<Result<FeatureDto>> CreateAsync(CreateFeatureRequest request, CancellationToken ct)
    {
        await using var scope = await unitOfWork.BeginAsync(ct);
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, ct);
        if (actor.IsFailure)
        {
            return Result<FeatureDto>.Failure(actor.Errors[0]);
        }
        var created = Feature.Create(request.ProductId, request.Slug, request.Title, request.DescriptionMarkdown, clock);
        if (created.IsFailure)
        {
            return Result<FeatureDto>.Failure(created.Error!.ToError());
        }
        var f = created.Value;
        if (await products.GetByIdAsync(f.ProductId, ct) is null)
        {
            return Result<FeatureDto>.Failure(KbErrors.ProductNotFoundInBody());
        }
        if (renderer.IsTooComplex(f.DescriptionMarkdown))
        {
            return Result<FeatureDto>.Failure(KbErrors.BodyTooComplex());
        }
        if (await features.SlugTakenAsync(f.ProductId, f.Slug, ct))
        {
            return Result<FeatureDto>.Failure(SlugTaken());
        }
        features.Add(f);
        AdminAudit.Record(events, AdminEventType.FeatureCreated, actor.Value, AdminSubjectType.Feature, f.Id, new
        {
            f.ProductId
        }, clock);
        var committed = await scope.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return Result<FeatureDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? SlugTaken() : committed.Errors[0]);
        }
        return await Saved(f, ct);
    }

    public async Task<Result<FeatureDto>> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken ct)
    {
        await using var scope = await unitOfWork.BeginAsync(ct);
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, ct);
        if (actor.IsFailure)
        {
            return Result<FeatureDto>.Failure(actor.Errors[0]);
        }
        var f = await features.GetAsync(id, ct);
        if (f is null)
        {
            return Result<FeatureDto>.Failure(Missing());
        }
        if (f.Version != request.Version)
        {
            return Result<FeatureDto>.Failure(Stale());
        }
        if (!Parse(request.ProgressStatus, out var status) || status is null)
        {
            return Result<FeatureDto>.Failure(InvalidStatus());
        }
        if (request.DescriptionMarkdown is { } body && renderer.IsTooComplex(body))
        {
            return Result<FeatureDto>.Failure(KbErrors.BodyTooComplex());
        }
        var changed = f.Update(request.Title, request.DescriptionMarkdown, status.Value, clock);
        if (changed.IsFailure)
        {
            return Result<FeatureDto>.Failure(changed.Error!.ToError());
        }
        features.Update(f);
        AdminAudit.Record(events, AdminEventType.FeatureUpdated, actor.Value, AdminSubjectType.Feature, f.Id, new
        {
            f.ProgressStatus
        }, clock);
        var committed = await scope.CommitAsync(ct);
        return committed.IsFailure ? Result<FeatureDto>.Failure(committed.Errors[0]) : await Saved(f, ct);
    }

    public async Task<Result<FeatureDto>> SetPublishedAsync(Guid id, bool published, uint version, CancellationToken ct)
    {
        await using var scope = await unitOfWork.BeginAsync(ct);
        var actor = await CurrentAgent.RequireActiveAsync(claims, agents, ct);
        if (actor.IsFailure)
        {
            return Result<FeatureDto>.Failure(actor.Errors[0]);
        }
        var f = await features.GetAsync(id, ct);
        if (f is null)
        {
            return Result<FeatureDto>.Failure(Missing());
        }
        if (f.Version != version)
        {
            return Result<FeatureDto>.Failure(Stale());
        }
        f.SetPublished(published, clock);
        features.Update(f);
        AdminAudit.Record(events, published ? AdminEventType.FeaturePublished : AdminEventType.FeatureUnpublished, actor.Value, AdminSubjectType.Feature, f.Id, new
        {
            f.PublicationState
        }, clock);
        var committed = await scope.CommitAsync(ct);
        return committed.IsFailure ? Result<FeatureDto>.Failure(committed.Errors[0]) : await Saved(f, ct);
    }

    private async Task<Result<FeatureDto>> Saved(Feature f, CancellationToken ct) => Result<FeatureDto>.Success(Dto(await features.GetAsync(f.Id, ct) ?? f));

    public async Task<Result<PagedResponse<PublicFeatureSummaryDto>>> ListPublicAsync(
        string key,
        string? q,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var product = await PublicProductScope.ResolveAsync(products, key, ct);
        if (product is null)
        {
            return Result<PagedResponse<PublicFeatureSummaryDto>>.Failure(Missing());
        }
        if (!Parse(status, out var s))
        {
            return Result<PagedResponse<PublicFeatureSummaryDto>>.Failure(InvalidStatus());
        }
        var r = await features.ListAsync(product.Id, true, s, q, page, pageSize, ct);
        return Result<PagedResponse<PublicFeatureSummaryDto>>.Success(new(r.Items.Where(f => f.ProductId == product.Id && f.PublicationState == FeaturePublicationState.Published).Select(f => new PublicFeatureSummaryDto(f.Id, f.Slug, f.Title, f.ProgressStatus.ToString(), f.UpdatedAt)).ToArray(), r.Page, r.PageSize, r.TotalCount));
    }

    public async Task<Result<PublicFeatureDto>> GetPublicAsync(string key, string slug, CancellationToken ct)
    {
        var product = await PublicProductScope.ResolveAsync(products, key, ct);
        if (product is null || !PublicProductScope.IsSlug(slug, DomainLimits.KbSlugMaxLength))
        {
            return Result<PublicFeatureDto>.Failure(Missing());
        }
        var f = await features.GetBySlugAsync(product.Id, slug.Trim(), ct);
        if (f is null || f.ProductId != product.Id || f.PublicationState != FeaturePublicationState.Published)
        {
            return Result<PublicFeatureDto>.Failure(Missing());
        }
        return Result<PublicFeatureDto>.Success(new(f.Id, f.Slug, f.Title, renderer.Render(f.DescriptionMarkdown), f.ProgressStatus.ToString(), f.UpdatedAt));
    }

    public async Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> ListTicketsAsync(Guid id, CancellationToken ct)
    {
        if (await features.GetAsync(id, ct) is null)
        {
            return Result<IReadOnlyList<LinkedFeatureTicketDto>>.Failure(Missing());
        }
        return Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success(await features.ListTicketsAsync(id, ct));
    }
}
