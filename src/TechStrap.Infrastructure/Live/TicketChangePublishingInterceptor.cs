using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The post-commit half of the hook (D-018): publishes what <see cref="TicketChangeCaptureInterceptor"/> staged once the database transaction has really committed, and drops it when the
/// transaction rolls back or fails. <c>UnitOfWork</c> commits after SaveChanges, so SaveChanges' own "saved" event fires too early to be the post-commit point.
/// Nothing here can fail the commit that has already happened: <see cref="TicketChangePublisher"/> swallows and logs every error.
/// </summary>
internal sealed class TicketChangePublishingInterceptor(TicketChangePublisher publisher) : DbTransactionInterceptor
{
    public override void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) =>
        publisher.Publish(eventData.Context);

    public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(eventData.Context);

    public override void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) =>
        publisher.Discard(eventData.Context);

    public override Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(System.Data.Common.DbTransaction transaction, TransactionErrorEventData eventData) =>
        publisher.Discard(eventData.Context);

    public override Task TransactionFailedAsync(System.Data.Common.DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }
}
