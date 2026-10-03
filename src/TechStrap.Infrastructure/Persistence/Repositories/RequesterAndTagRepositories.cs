using Microsoft.EntityFrameworkCore;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class RequesterRepository(TechStrapDbContext context) : IRequesterRepository
{
    public async Task<Requester?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<RequesterRecord>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken))?.ToDomain();

    public async Task<Requester?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // The column is citext, so the comparison is case-insensitive in the database; lower-casing here keeps the
        // parameter in the same normalised form the domain stores.
        var normalised = email.Trim().ToLowerInvariant();
        return (await context.Set<RequesterRecord>().FirstOrDefaultAsync(r => r.Email == normalised, cancellationToken))?.ToDomain();
    }

    public void Add(Requester requester) => context.Set<RequesterRecord>().Add(requester.ToRecord());

    public void Update(Requester requester) => requester.CopyTo(context.FindLoaded<RequesterRecord>(requester.Id));
}

internal sealed class TagRepository(TechStrapDbContext context) : ITagRepository
{
    public async Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<TagRecord>().FirstOrDefaultAsync(t => t.Id == id, cancellationToken))?.ToDomain();

    public async Task<Tag?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        (await context.Set<TagRecord>().FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken)
    {
        var records = await context.Set<TagRecord>().AsNoTracking().OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        return [.. records.Select(t => t.ToDomain())];
    }

    public void Add(Tag tag) => context.Set<TagRecord>().Add(tag.ToRecord());

    public void Update(Tag tag) => tag.CopyTo(context.FindLoaded<TagRecord>(tag.Id));

    public void Remove(Tag tag) => context.Set<TagRecord>().Remove(context.FindLoaded<TagRecord>(tag.Id));
}
