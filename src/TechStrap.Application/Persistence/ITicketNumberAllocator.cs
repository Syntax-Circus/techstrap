using SyntaxCircus.Common;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Persistence;

public interface ITicketNumberAllocator
{
    /// <summary>
    /// Takes the next number for the product (D-009). Must run inside an <see cref="IUnitOfWorkScope"/>: the counter row is locked
    /// until that transaction ends, so concurrent creators queue up, and a rollback gives the number back (no gaps).
    /// Returns NotFound when the product does not exist.
    /// </summary>
    Task<Result<TicketNumber>> AllocateAsync(Guid productId, CancellationToken cancellationToken);
}
