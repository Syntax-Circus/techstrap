using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain;
using TechStrap.Domain.Outbox;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

/// <summary>Enqueue: stages a row for the caller's unit of work (D-010).</summary>
internal sealed class EmailOutbox(TechStrapDbContext context) : IEmailOutbox
{
    public void Enqueue(EmailOutboxItem item) => context.Set<EmailOutboxRecord>().Add(item.ToRecord());
}

/// <summary>
/// The worker and admin side of the outbox. Claiming uses <c>FOR UPDATE SKIP LOCKED</c> inside its own short transaction, so
/// concurrent workers never receive the same row; an expired claim (a crashed worker) can be claimed again.
/// </summary>
internal sealed class EmailOutboxStore(TechStrapDbContext context, TimeProvider clock) : IEmailOutboxStore
{
    /// <summary>
    /// Due Pending rows, plus Sending rows whose lease expired or is missing. <c>{0}</c> is now and <c>{1}</c> the limit (positional
    /// <c>FromSqlRaw</c> parameters, never concatenated). Each branch is served by its own partial index.
    /// </summary>
    internal const string ClaimSql =
        """
        SELECT * FROM email_outbox
        WHERE (status = 'Pending' AND next_attempt_at <= {0})
           OR (status = 'Sending' AND (locked_until IS NULL OR locked_until <= {0}))
        ORDER BY next_attempt_at, id
        LIMIT {1}
        FOR UPDATE SKIP LOCKED
        """;

    public async Task<IReadOnlyList<EmailOutboxItem>> ClaimBatchAsync(string workerId, int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        var limit = Paging.NormalizeBatchSize(batchSize);

        // Whole microseconds, as the Domain stores them, so the comparison matches what the Domain decides.
        var utcNow = clock.GetUtcNow();
        var now = utcNow.AddTicks(-(utcNow.Ticks % 10));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var due = await context.Set<EmailOutboxRecord>().FromSqlRaw(ClaimSql, now, limit).ToListAsync(cancellationToken);

        var claimed = new List<EmailOutboxItem>(due.Count);
        foreach (var record in due)
        {
            var item = record.ToDomain();
            var outcome = item.Claim(workerId, lease, clock);
            if (outcome.IsSuccess)
            {
                item.CopyTo(record);
                claimed.Add(item);
            }
            else if (outcome.Error!.Code == PersistenceErrorCodes.OutboxDeadLettered)
            {
                // The Domain already moved the item to DeadLettered: persist that, but it is not part of the batch.
                item.CopyTo(record);
            }
            else if (outcome.Error.Kind == DomainErrorKind.Validation)
            {
                throw new ArgumentException($"The claim was refused: {outcome.Error.Code}.", nameof(workerId));
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return claimed;
    }

    public Task<Result> MarkSentAsync(Guid id, string workerId, CancellationToken cancellationToken) =>
        ApplyAsync(id, item => item.MarkSent(workerId, clock), cancellationToken);

    public Task<Result> MarkFailedAsync(Guid id, string workerId, string error, CancellationToken cancellationToken) =>
        ApplyAsync(id, item => item.MarkFailed(workerId, error, clock), cancellationToken);

    public async Task<PagedResult<EmailOutboxItem>> ListDeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Paging.NormalizePage(page);
        pageSize = Paging.NormalizePageSize(pageSize);

        var deadLetters = context.Set<EmailOutboxRecord>().AsNoTracking().Where(e => e.Status == OutboxStatus.DeadLettered);
        var total = await deadLetters.CountAsync(cancellationToken);
        var records = await deadLetters.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<EmailOutboxItem>([.. records.Select(e => e.ToDomain())], page, pageSize, total);
    }

    public async Task<EmailOutboxItem?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<EmailOutboxRecord>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken))?.ToDomain();

    public void Update(EmailOutboxItem item) => item.CopyTo(context.FindLoaded<EmailOutboxRecord>(item.Id));

    /// <summary>
    /// Locks the row for the duration of one short transaction, so a reclaim cannot slip in between the ownership check and the save.
    /// The Domain decides ownership (<c>outbox-not-claim-owner</c>) and a refused change leaves the row untouched.
    /// </summary>
    private async Task<Result> ApplyAsync(Guid id, Func<EmailOutboxItem, DomainResult> change, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var record = (await context.Set<EmailOutboxRecord>().FromSqlRaw("SELECT * FROM email_outbox WHERE id = {0} FOR UPDATE", id).ToListAsync(cancellationToken)).SingleOrDefault();
        if (record is null)
        {
            return Result.Failure(new ResultError(PersistenceErrorCodes.OutboxNotFound, "The email does not exist.", ResultErrorKind.NotFound));
        }

        var item = record.ToDomain();
        var outcome = change(item);
        if (outcome.IsFailure)
        {
            context.ChangeTracker.Clear();
            return outcome.ToResult();
        }

        item.CopyTo(record);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return Result.Success();
    }
}
