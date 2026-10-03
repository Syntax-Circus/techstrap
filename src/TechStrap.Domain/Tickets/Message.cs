using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

public enum AuthorType
{
    Requester,
    Agent,
    System,
}

public enum MessageVisibility
{
    Public,
    Internal,
}

/// <summary>A message on a ticket. The body is already sanitized (PHASE-05); this type never sanitizes.</summary>
public sealed class Message
{
    private readonly List<Attachment> _newAttachments = [];

    private Message(
        Guid id,
        Guid ticketId,
        AuthorType authorType,
        Guid? authorId,
        MessageVisibility visibility,
        string body,
        string? messageId,
        string? inReplyTo,
        DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        AuthorType = authorType;
        AuthorId = authorId;
        Visibility = visibility;
        Body = body;
        MessageId = messageId;
        InReplyTo = inReplyTo;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public AuthorType AuthorType { get; }

    /// <summary>The agent or requester id; null for System.</summary>
    public Guid? AuthorId { get; }

    public MessageVisibility Visibility { get; }

    public string Body { get; }

    /// <summary>Reserved for inbound email (RFC 5322 Message-ID). Unused until a later phase.</summary>
    public string? MessageId { get; private set; }

    /// <summary>Reserved for inbound email (In-Reply-To). Unused until a later phase.</summary>
    public string? InReplyTo { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsVisibleToCustomer => Visibility == MessageVisibility.Public;

    /// <summary>Attachments added in this session and not yet persisted.</summary>
    public IReadOnlyList<Attachment> NewAttachments => _newAttachments;

    public static DomainResult<Message> Create(
        Guid ticketId,
        AuthorType authorType,
        Guid? authorId,
        MessageVisibility visibility,
        string? body,
        TimeProvider clock) =>
        CreateAt(ticketId, authorType, authorId, visibility, body, DomainTime.Now(clock), clock);

    /// <summary>Like <see cref="Create"/> with an explicit creation time; the ticket uses it to keep message times strictly increasing.</summary>
    internal static DomainResult<Message> CreateAt(
        Guid ticketId,
        AuthorType authorType,
        Guid? authorId,
        MessageVisibility visibility,
        string? body,
        DateTimeOffset createdAt,
        TimeProvider clock)
    {
        if (!Enum.IsDefined(authorType))
        {
            return DomainErrors.Validation("author-type-invalid", "The author type is not valid.", "author-type");
        }

        if (!Enum.IsDefined(visibility))
        {
            return DomainErrors.Validation("visibility-invalid", "The visibility is not valid.", "visibility");
        }

        var text = Guard.RequiredText(body, DomainLimits.MessageBodyMaxLength, "body");
        if (text.IsFailure)
        {
            return text.Error!;
        }

        if (authorType == AuthorType.Requester && visibility == MessageVisibility.Internal)
        {
            return DomainErrors.Validation("internal-message-by-requester", "A requester cannot write an internal message.", "visibility");
        }

        if (authorType != AuthorType.System && (authorId is null || authorId == Guid.Empty))
        {
            return DomainErrors.Validation("author-required", "An agent or requester message needs an author id.", "author-id");
        }

        var author = authorType == AuthorType.System ? null : authorId;
        return DomainResult<Message>.Ok(new Message(EntityId.New(clock), ticketId, authorType, author, visibility, text.Value, null, null, createdAt));
    }

    public static Message Restore(
        Guid id,
        Guid ticketId,
        AuthorType authorType,
        Guid? authorId,
        MessageVisibility visibility,
        string body,
        string? messageId,
        string? inReplyTo,
        DateTimeOffset createdAt) =>
        new(id, ticketId, authorType, authorId, visibility, body, messageId, inReplyTo, createdAt);

    public DomainResult<Attachment> AddAttachment(string? fileName, string? contentType, long size, string? storageKey, TimeProvider clock)
    {
        var attachment = Attachment.Create(TicketId, Id, fileName, contentType, size, storageKey, clock);
        if (attachment.IsSuccess)
        {
            _newAttachments.Add(attachment.Value);
        }

        return attachment;
    }

    /// <summary>Records the reserved email identifiers (stored, not used yet).</summary>
    public DomainResult SetEmailIdentifiers(string? messageId, string? inReplyTo)
    {
        var id = Guard.OptionalText(messageId, DomainLimits.MessageIdMaxLength, "message-id");
        var reply = Guard.OptionalText(inReplyTo, DomainLimits.MessageIdMaxLength, "in-reply-to");
        if (Guard.FirstError(id, reply) is { } error)
        {
            return error;
        }

        MessageId = id.Value;
        InReplyTo = reply.Value;
        return DomainResult.Ok();
    }

    internal void AcceptChanges() => _newAttachments.Clear();
}
