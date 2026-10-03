using TechStrap.Domain.Requesters;

namespace TechStrap.Application.Persistence;

public interface IRequesterRepository
{
    Task<Requester?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Case-insensitive.</summary>
    Task<Requester?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    void Add(Requester requester);

    void Update(Requester requester);
}
