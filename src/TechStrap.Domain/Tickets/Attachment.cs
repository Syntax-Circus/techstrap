using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

/// <summary>Metadata of a stored file on a message. The bytes live behind <c>IAttachmentStore</c> under <see cref="StorageKey"/>.</summary>
public sealed class Attachment
{
    private Attachment(Guid id, Guid ticketId, Guid messageId, string fileName, string contentType, long size, string storageKey, DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        MessageId = messageId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        StorageKey = storageKey;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public Guid MessageId { get; }

    public string FileName { get; }

    public string ContentType { get; }

    public long Size { get; }

    public string StorageKey { get; }

    public DateTimeOffset CreatedAt { get; }

    public static DomainResult<Attachment> Create(
        Guid ticketId,
        Guid messageId,
        string? fileName,
        string? contentType,
        long size,
        string? storageKey,
        TimeProvider clock)
    {
        var name = Guard.RequiredText(fileName, DomainLimits.FileNameMaxLength, "file-name");
        var type = Guard.RequiredText(contentType, DomainLimits.ContentTypeMaxLength, "content-type");
        var key = Guard.RequiredText(storageKey, DomainLimits.StorageKeyMaxLength, "storage-key");
        if (Guard.FirstError(name, type, key) is { } error)
        {
            return error;
        }

        if (name.Value.IndexOfAny(['/', '\\']) >= 0 || name.Value.Any(char.IsControl))
        {
            return DomainErrors.Validation("file-name-invalid", "A file name has no path separators or control characters.", "file-name");
        }

        if (!type.Value.Contains('/'))
        {
            return DomainErrors.Validation("content-type-invalid", "A content type looks like type/subtype.", "content-type");
        }

        if (size is < 1 or > DomainLimits.AttachmentMaxBytes)
        {
            return DomainErrors.Validation("size-invalid", $"An attachment is 1 byte to {DomainLimits.AttachmentMaxBytes} bytes.", "size");
        }

        return DomainResult<Attachment>.Ok(new Attachment(EntityId.New(clock), ticketId, messageId, name.Value, type.Value, size, key.Value, clock.GetUtcNow()));
    }

    public static Attachment Restore(Guid id, Guid ticketId, Guid messageId, string fileName, string contentType, long size, string storageKey, DateTimeOffset createdAt) =>
        new(id, ticketId, messageId, fileName, contentType, size, storageKey, createdAt);
}
