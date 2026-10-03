using TechStrap.Domain.Outbox;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

internal static class OperationsMappings
{
    public static EmailOutboxItem ToDomain(this EmailOutboxRecord record) =>
        EmailOutboxItem.Restore(
            record.Id, record.Kind, record.ToAddress, record.Payload, record.ProductId, record.TicketId, record.Status, record.Attempts, record.NextAttemptAt,
            record.ClaimedBy, record.LockedUntil, record.LastError, record.CreatedAt, record.SentAt);

    public static EmailOutboxRecord ToRecord(this EmailOutboxItem item)
    {
        var record = new EmailOutboxRecord
        {
            Id = item.Id,
            Kind = item.Kind,
            ToAddress = item.ToAddress,
            Payload = item.PayloadJson,
            ProductId = item.ProductId,
            TicketId = item.TicketId,
            CreatedAt = item.CreatedAt,
        };
        item.CopyTo(record);
        return record;
    }

    /// <summary>Copies the delivery state. The kind, address, payload and links never change.</summary>
    public static void CopyTo(this EmailOutboxItem item, EmailOutboxRecord record)
    {
        record.Status = item.Status;
        record.Attempts = item.Attempts;
        record.NextAttemptAt = item.NextAttemptAt;
        record.ClaimedBy = item.ClaimedBy;
        record.LockedUntil = item.LockedUntil;
        record.LastError = item.LastError;
        record.SentAt = item.SentAt;
    }
}
