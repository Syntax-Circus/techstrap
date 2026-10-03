using System.Globalization;
using System.Text.RegularExpressions;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

/// <summary>Metadata of a stored file on a message. The bytes live behind <c>IAttachmentStore</c> under <see cref="StorageKey"/>.</summary>
public sealed partial class Attachment
{
    // RFC 6838 restricted-name on both sides of a single slash; no parameters, whitespace or control characters.
    private const string ContentTypePattern = @"\A[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}/[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}\z";

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

        if (name.Value.IndexOfAny(['/', '\\']) >= 0 || name.Value is "." or ".." || name.Value.Any(IsControlOrFormat))
        {
            return DomainErrors.Validation("file-name-invalid", "A file name has no path separators or control characters.", "file-name");
        }

        if (!ContentTypeRegex().IsMatch(contentType ?? string.Empty))
        {
            return DomainErrors.Validation("content-type-invalid", "A content type looks like type/subtype.", "content-type");
        }

        if (size is < 1 or > DomainLimits.AttachmentMaxBytes)
        {
            return DomainErrors.Validation("size-invalid", $"An attachment is 1 byte to {DomainLimits.AttachmentMaxBytes} bytes.", "size");
        }

        return DomainResult<Attachment>.Ok(new Attachment(EntityId.New(clock), ticketId, messageId, name.Value, type.Value, size, key.Value, DomainTime.Now(clock)));
    }

    [GeneratedRegex(ContentTypePattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ContentTypeRegex();

    private static bool IsControlOrFormat(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format;

    public static Attachment Restore(Guid id, Guid ticketId, Guid messageId, string fileName, string contentType, long size, string storageKey, DateTimeOffset createdAt) =>
        new(id, ticketId, messageId, fileName, contentType, size, storageKey, createdAt);
}
