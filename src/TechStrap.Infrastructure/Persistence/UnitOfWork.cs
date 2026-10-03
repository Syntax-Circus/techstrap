using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// EF unit of work over the scoped <see cref="TechStrapDbContext"/>: one explicit transaction per scope, one SaveChanges on
/// commit, and recoverable database failures translated to Conflict results so handlers never see a raw EF exception.
/// </summary>
internal sealed class UnitOfWork(TechStrapDbContext context) : IUnitOfWork
{
    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A unit of work is already active on this context; scopes do not nest.");
        }

        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        return new Scope(context, transaction);
    }

    private sealed class Scope(TechStrapDbContext context, IDbContextTransaction transaction) : IUnitOfWorkScope
    {
        private bool _finished;

        public async Task<Result> CommitAsync(CancellationToken cancellationToken)
        {
            if (_finished)
            {
                throw new InvalidOperationException("This unit of work scope has already been committed or rolled back.");
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _finished = true;
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                await RollBackAsync();
                return Conflict(PersistenceErrorCodes.ConcurrencyConflict, "The record was changed by someone else. Reload and try again.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await RollBackAsync();
                return Conflict(PersistenceErrorCodes.Duplicate, "A record with the same unique value already exists.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                await RollBackAsync();
                return Conflict(PersistenceErrorCodes.ReferenceViolation, "The record refers to, or is still used by, another record.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_finished)
                {
                    await RollBackAsync();
                }
            }
            catch (Exception exception) when (exception is DbException or InvalidOperationException)
            {
                // A failed rollback (for example a dead connection) must not replace the exception that is already unwinding; the server
                // discards an unfinished transaction when the connection goes away.
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }

        private static Result Conflict(string code, string message) =>
            Result.Failure(new ResultError(code, message, ResultErrorKind.Conflict));

        private async Task RollBackAsync()
        {
            _finished = true;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                // Staged changes belong to the rolled-back transaction; do not let them leak into the next scope on this context.
                context.ChangeTracker.Clear();
            }
        }
    }
}
