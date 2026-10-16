using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Features;
using TechStrap.Domain.Features;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class FeatureRepository(TechStrapDbContext context) : IFeatureRepository
{
    private static Feature Map(
        FeatureRecord r) =>
        Feature.Restore(r.Id, r.ProductId, r.Slug, r.Title, r.DescriptionMarkdown, r.PublicationState, r.ProgressStatus, r.CreatedAt, r.UpdatedAt, r.Version);

    public async Task<Feature?> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await context.Set<FeatureRecord>().FirstOrDefaultAsync(f => f.Id == id, ct);
        return r is null ? null : Map(r);
    }

    public async Task<Feature?> GetBySlugAsync(Guid productId, string slug, CancellationToken ct)
    {
        var r = await context.Set<FeatureRecord>().AsNoTracking().FirstOrDefaultAsync(f => f.ProductId == productId && f.Slug == slug, ct);
        return r is null ? null : Map(r);
    }

    public Task<bool> SlugTakenAsync(
        Guid productId,
        string slug,
        CancellationToken ct) =>
        context.Set<FeatureRecord>().AnyAsync(f => f.ProductId == productId && f.Slug == slug, ct);

    public async Task<PagedResult<Feature>> ListAsync(
        Guid? productId,
        bool publishedOnly,
        FeatureProgressStatus? status,
        string? q,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Paging.NormalizePage(page);
        pageSize = Paging.NormalizePageSize(pageSize);
        var query = context.Set<FeatureRecord>().AsNoTracking();
        if (productId is { } p)
        {
            query = query.Where(f => f.ProductId == p);
        }
        if (publishedOnly)
        {
            query = query.Where(f => f.PublicationState == FeaturePublicationState.Published);
        }
        if (status is { } s)
        {
            query = query.Where(f => f.ProgressStatus == s);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim().ToLowerInvariant();
            if (text.Length > 500)
            {
                text = text[..500];
            }
            query = query.Where(f => f.Title.ToLower().Contains(text) || f.DescriptionMarkdown.ToLower().Contains(text));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(f => f.UpdatedAt).ThenByDescending(f => f.Id).Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(Map).ToArray(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<LinkedFeatureTicketDto>> ListTicketsAsync(
        Guid featureId,
        CancellationToken ct) =>
        await context.Set<TicketRecord>().AsNoTracking().Where(t => t.FeatureId == featureId).OrderByDescending(t => t.LastActivityAt).Select(t => new LinkedFeatureTicketDto(t.Id, t.Number, t.Subject, t.Status.ToString(), t.Version)).ToListAsync(ct);

    public void Add(Feature f)
    {
        var r = new FeatureRecord
        {
            Id = f.Id,
            ProductId = f.ProductId,
            Slug = f.Slug,
            CreatedAt = f.CreatedAt
        };
        Copy(f, r);
        context.Set<FeatureRecord>().Add(r);
    }

    public void Update(Feature f)
    {
        var r = context.FindLoaded<FeatureRecord>(f.Id);
        Copy(f, r);
        context.ApplyOriginalVersion(r, f.Version);
    }

    private static void Copy(Feature f, FeatureRecord r)
    {
        r.Title = f.Title;
        r.DescriptionMarkdown = f.DescriptionMarkdown;
        r.PublicationState = f.PublicationState;
        r.ProgressStatus = f.ProgressStatus;
        r.UpdatedAt = f.UpdatedAt;
    }
}
