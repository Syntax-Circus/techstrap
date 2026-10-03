using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// Per-product ticket numbers from a counter row in <c>product_ticket_sequences</c> (D-009). One statement takes the number:
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> creates the counter on the product's first ticket (two creators racing
/// on that first ticket are serialised by the unique key) and otherwise advances it under the row lock, so concurrent creators
/// for the same product queue behind each other until the first transaction ends. A rollback undoes the increment, so there are
/// no gaps and no duplicates. The counter is a separate row, so the product row is never rewritten: its <c>xmin</c> concurrency
/// token does not change when a ticket is created and a product edit in flight is not disturbed. It must run in the
/// transaction of the ticket being created.
/// </summary>
internal sealed class TicketNumberAllocator(TechStrapDbContext context) : ITicketNumberAllocator
{
    private const string AllocateSql =
        """
        WITH taken AS (
            INSERT INTO product_ticket_sequences (product_id, next_number)
            SELECT id, 2 FROM products WHERE id = @id
            ON CONFLICT (product_id) DO UPDATE SET next_number = product_ticket_sequences.next_number + 1
            RETURNING next_number - 1 AS number
        )
        SELECT p.number_prefix, taken.number FROM products p CROSS JOIN taken WHERE p.id = @id
        """;

    public async Task<Result<TicketNumber>> AllocateAsync(Guid productId, CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Ticket numbers must be allocated inside an IUnitOfWork scope.");

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(AllocateSql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("id", productId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<TicketNumber>.Failure(DomainErrors.NotFound("product-not-found", "The product does not exist.").ToError());
        }

        return TicketNumber.Create(reader.GetString(0), reader.GetInt64(1)).ToResult();
    }
}
