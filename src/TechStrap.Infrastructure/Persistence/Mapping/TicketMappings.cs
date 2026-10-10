using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

/// <summary>Maps the ticket aggregate and its parts to and from persistence records (D-026).</summary>
internal static class TicketMappings
{
    /// <summary>The record must have its <see cref="TicketRecord.Tags"/> loaded.</summary>
    public static Ticket ToDomain(this TicketRecord record)
    {
        if (!TicketNumber.TryParse(record.Number, out var number))
        {
            throw new InvalidOperationException($"Stored ticket number '{record.Number}' is not a valid ticket number.");
        }

        return Ticket.Restore(
            record.Id, number, record.ProductId, record.RequesterId, record.Subject, record.Status, record.Priority, record.AssigneeId, record.Channel,
            record.IsSpam, record.ParentTicketId, record.Metadata, record.MetadataTrusted, record.CustomFields, record.CreatedAt, record.FirstResponseAt,
            record.SolvedAt, record.ClosedAt, record.LastActivityAt, record.Tags.Select(t => t.TagId), record.Version);
    }

    public static TicketRecord ToRecord(this Ticket ticket)
    {
        var record = new TicketRecord
        {
            Id = ticket.Id,
            Number = ticket.Number.ToString(),
            RequesterId = ticket.RequesterId,
            Subject = ticket.Subject,
            Channel = ticket.Channel,
            ParentTicketId = ticket.ParentTicketId,
            Metadata = ticket.MetadataJson,
            MetadataTrusted = ticket.MetadataTrusted,
            CustomFields = ticket.CustomFieldsJson,
            CreatedAt = ticket.CreatedAt,
        };
        ticket.CopyTo(record);
        return record;
    }

    /// <summary>Copies the mutable fields and synchronizes the tag links. The number, subject and requester never change.</summary>
    public static void CopyTo(this Ticket ticket, TicketRecord record)
    {
        record.ProductId = ticket.ProductId;
        record.Status = ticket.Status;
        record.Priority = ticket.Priority;
        record.AssigneeId = ticket.AssigneeId;
        record.IsSpam = ticket.IsSpam;
        record.FirstResponseAt = ticket.FirstResponseAt;
        record.SolvedAt = ticket.SolvedAt;
        record.ClosedAt = ticket.ClosedAt;
        record.LastActivityAt = ticket.LastActivityAt;

        record.Tags.RemoveAll(link => !ticket.TagIds.Contains(link.TagId));
        foreach (var tagId in ticket.TagIds.Where(id => record.Tags.All(link => link.TagId != id)))
        {
            record.Tags.Add(new TicketTagRecord { TicketId = ticket.Id, TagId = tagId });
        }
    }

    public static Message ToDomain(this MessageRecord record) =>
        Message.Restore(record.Id, record.TicketId, record.AuthorType, record.AuthorId, record.Visibility, record.Body, record.MessageId, record.InReplyTo, record.CreatedAt);

    public static MessageRecord ToRecord(this Message message) => new()
    {
        Id = message.Id,
        TicketId = message.TicketId,
        AuthorType = message.AuthorType,
        AuthorId = message.AuthorId,
        Visibility = message.Visibility,
        Body = message.Body,
        MessageId = message.MessageId,
        InReplyTo = message.InReplyTo,
        CreatedAt = message.CreatedAt,
    };

    public static Attachment ToDomain(this AttachmentRecord record) =>
        Attachment.Restore(record.Id, record.TicketId, record.MessageId, record.FileName, record.ContentType, record.Size, record.StorageKey, record.CreatedAt);

    public static AttachmentRecord ToRecord(this Attachment attachment) => new()
    {
        Id = attachment.Id,
        TicketId = attachment.TicketId,
        MessageId = attachment.MessageId,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        Size = attachment.Size,
        StorageKey = attachment.StorageKey,
        CreatedAt = attachment.CreatedAt,
    };

    public static TicketEvent ToDomain(this TicketEventRecord record) =>
        TicketEvent.Restore(record.Id, record.TicketId, record.Type, record.ActorType, record.ActorId, record.Payload, record.OccurredAt);

    public static TicketEventRecord ToRecord(this TicketEvent ticketEvent) => new()
    {
        Id = ticketEvent.Id,
        TicketId = ticketEvent.TicketId,
        Type = ticketEvent.Type,
        ActorType = ticketEvent.ActorType,
        ActorId = ticketEvent.ActorId,
        Payload = ticketEvent.PayloadJson,
        OccurredAt = ticketEvent.OccurredAt,
    };

    public static TicketAccessToken ToDomain(this TicketAccessTokenRecord record) =>
        TicketAccessToken.Restore(record.Id, record.TicketId, record.RequesterId, record.TokenHash, record.IssuedAt, record.ExpiresAt, record.RevokedAt, record.LastUsedAt);

    public static TicketAccessTokenRecord ToRecord(this TicketAccessToken token) => new()
    {
        Id = token.Id,
        TicketId = token.TicketId,
        RequesterId = token.RequesterId,
        TokenHash = token.TokenHash,
        IssuedAt = token.IssuedAt,
        ExpiresAt = token.ExpiresAt,
        RevokedAt = token.RevokedAt,
        LastUsedAt = token.LastUsedAt,
    };

    public static void CopyTo(this TicketAccessToken token, TicketAccessTokenRecord record)
    {
        record.ExpiresAt = token.ExpiresAt;
        record.RevokedAt = token.RevokedAt;
        record.LastUsedAt = token.LastUsedAt;
    }
}
