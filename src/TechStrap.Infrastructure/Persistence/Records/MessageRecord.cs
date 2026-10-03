using NpgsqlTypes;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class MessageRecord
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }

    public AuthorType AuthorType { get; set; }

    public Guid? AuthorId { get; set; }

    public MessageVisibility Visibility { get; set; }

    /// <summary>Sanitized body (PHASE-05).</summary>
    public string Body { get; set; } = string.Empty;

    public string? MessageId { get; set; }

    public string? InReplyTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Generated column (D-027): the body, weight B. Never written by the application.</summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}

internal sealed class AttachmentRecord
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }

    public Guid MessageId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
