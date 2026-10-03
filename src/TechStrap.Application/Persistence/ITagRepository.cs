using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Persistence;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tag?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken);

    void Add(Tag tag);

    void Update(Tag tag);

    /// <summary>Stages a delete. Commit returns a Conflict ("reference-violation") while tickets still carry the tag.</summary>
    void Remove(Tag tag);
}
