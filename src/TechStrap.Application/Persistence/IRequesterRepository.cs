using TechStrap.Domain.Requesters;

namespace TechStrap.Application.Persistence;

/// <summary>
/// Requesters. <c>Update</c> requires the record to have been loaded in the current scope and checks the version the Domain object carries
/// (the one the caller originally saw), so a copy loaded in an earlier request conflicts if the row changed since, for example if the
/// requester was erased in between.
/// </summary>
public interface IRequesterRepository
{
    Task<Requester?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Case-insensitive.</summary>
    Task<Requester?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    void Add(Requester requester);

    void Update(Requester requester);
}
