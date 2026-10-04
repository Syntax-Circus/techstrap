using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Persistence;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tag?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Every tag with the number of tickets that carry it (any status, spam included), ordered by name. One grouped query; read only.</summary>
    Task<IReadOnlyList<TagUsage>> ListWithTicketCountsAsync(CancellationToken cancellationToken);

    void Add(Tag tag);

    void Update(Tag tag);

    /// <summary>Stages a delete. Commit returns a Conflict ("reference-violation") while tickets still carry the tag.</summary>
    void Remove(Tag tag);
}

/// <summary>A tag and how many tickets carry it.</summary>
public sealed record TagUsage(Tag Tag, int TicketCount);
