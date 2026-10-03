using SyntaxCircus.Common;

namespace TechStrap.Application.Attachments;

/// <summary>A file the customer uploaded, as a readable stream. <see cref="Length"/> is the declared length.</summary>
public sealed record IncomingAttachment(string FileName, string? DeclaredContentType, long Length, Stream Content);

/// <summary>A stored file: a random storage key, a safe display name and the canonical content type of the matched kind.</summary>
public sealed record StoredAttachment(string StorageKey, string FileName, string ContentType, long Size);

/// <summary>
/// Ticket attachments over SyntaxCircus.Storage (D-032). Validates size, type (declared and leading bytes) and stores under a
/// random key; never uses the customer's file name as a path.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>Validation errors: attachment-too-large, attachment-empty, attachment-type-not-allowed (target "attachments").</summary>
    Task<Result<StoredAttachment>> SaveAsync(Guid ticketId, IncomingAttachment file, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
