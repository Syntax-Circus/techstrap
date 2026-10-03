using System.Text.Json;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

public enum TicketChannel
{
    Web,
    Api,
}

/// <summary>
/// The ticket aggregate. Every mutation checks the invariants (Closed is read-only, the transition table), changes state and
/// raises exactly one <see cref="TicketEvent"/> (a reply also raises a status event when it moves the status). Events and new
/// messages wait in <see cref="PendingEvents"/> / <see cref="PendingMessages"/> until a repository persists them in the same
/// transaction as the state change.
/// </summary>
public sealed class Ticket
{
    private readonly HashSet<Guid> _tagIds;
    private readonly List<TicketEvent> _pendingEvents = [];
    private readonly List<Message> _pendingMessages = [];
    private DateTimeOffset _lastStamp = DateTimeOffset.MinValue;

    private Ticket(
        Guid id,
        TicketNumber number,
        Guid productId,
        Guid requesterId,
        string subject,
        TicketStatus status,
        TicketPriority priority,
        Guid? assigneeId,
        TicketChannel channel,
        bool isSpam,
        Guid? parentTicketId,
        string? metadataJson,
        bool metadataTrusted,
        string? customFieldsJson,
        DateTimeOffset createdAt,
        DateTimeOffset? firstResponseAt,
        DateTimeOffset? solvedAt,
        DateTimeOffset? closedAt,
        DateTimeOffset lastActivityAt,
        IEnumerable<Guid> tagIds,
        uint version)
    {
        Id = id;
        Number = number;
        ProductId = productId;
        RequesterId = requesterId;
        Subject = subject;
        Status = status;
        Priority = priority;
        AssigneeId = assigneeId;
        Channel = channel;
        IsSpam = isSpam;
        ParentTicketId = parentTicketId;
        MetadataJson = metadataJson;
        MetadataTrusted = metadataTrusted;
        CustomFieldsJson = customFieldsJson;
        CreatedAt = createdAt;
        FirstResponseAt = firstResponseAt;
        SolvedAt = solvedAt;
        ClosedAt = closedAt;
        LastActivityAt = lastActivityAt;
        _tagIds = [.. tagIds];
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>Immutable (D-009): never changes, not even when the ticket moves to another product.</summary>
    public TicketNumber Number { get; }

    public Guid ProductId { get; private set; }

    public Guid RequesterId { get; }

    public string Subject { get; }

    public TicketStatus Status { get; private set; }

    public TicketPriority Priority { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public TicketChannel Channel { get; }

    public bool IsSpam { get; private set; }

    /// <summary>Set on a follow-up ticket: the Closed ticket the customer replied to (D-008).</summary>
    public Guid? ParentTicketId { get; }

    /// <summary>Device and app metadata as a JSON object, or null.</summary>
    public string? MetadataJson { get; }

    /// <summary>False when the metadata arrived through a Public key.</summary>
    public bool MetadataTrusted { get; }

    /// <summary>Reserved (jsonb) for later phases.</summary>
    public string? CustomFieldsJson { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? FirstResponseAt { get; private set; }

    public DateTimeOffset? SolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public IReadOnlyCollection<Guid> TagIds => _tagIds;

    /// <summary>
    /// Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>). Handlers that accept a client-supplied token compare
    /// it with this value; the persistence layer enforces it on save.
    /// </summary>
    public uint Version { get; }

    public IReadOnlyList<TicketEvent> PendingEvents => _pendingEvents;

    public IReadOnlyList<Message> PendingMessages => _pendingMessages;

    public static DomainResult<Ticket> Create(
        TicketNumber number,
        Guid productId,
        Guid requesterId,
        string? subject,
        TicketChannel channel,
        string? metadataJson,
        bool metadataTrusted,
        TimeProvider clock) =>
        CreateCore(number, productId, requesterId, subject, channel, metadataJson, metadataTrusted, null, clock);

    public static Ticket Restore(
        Guid id,
        TicketNumber number,
        Guid productId,
        Guid requesterId,
        string subject,
        TicketStatus status,
        TicketPriority priority,
        Guid? assigneeId,
        TicketChannel channel,
        bool isSpam,
        Guid? parentTicketId,
        string? metadataJson,
        bool metadataTrusted,
        string? customFieldsJson,
        DateTimeOffset createdAt,
        DateTimeOffset? firstResponseAt,
        DateTimeOffset? solvedAt,
        DateTimeOffset? closedAt,
        DateTimeOffset lastActivityAt,
        IEnumerable<Guid> tagIds,
        uint version)
    {
        var ticket = new Ticket(
            id, number, productId, requesterId, subject, status, priority, assigneeId, channel, isSpam, parentTicketId, metadataJson, metadataTrusted,
            customFieldsJson, createdAt, firstResponseAt, solvedAt, closedAt, lastActivityAt, tagIds, version);

        // FollowUpCreated stamps may sit past lastActivityAt (see CreateFollowUp); that is safe because a Closed ticket only ever gains
        // more FollowUpCreated events.
        // New events and messages must sort after everything already stored, even when the clock reads the same instant.
        ticket._lastStamp = lastActivityAt;
        return ticket;
    }

    public DomainResult ChangeStatus(TicketStatus to, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (!TicketStatusRules.CanTransition(Status, to))
        {
            return DomainErrors.Conflict("invalid-status-transition", $"A {Status} ticket cannot become {to}.");
        }

        ApplyStatus(to, actor, DomainTime.Now(clock), clock);
        return DomainResult.Ok();
    }

    /// <summary>A public agent reply: first response time, a Pending status when New or Open (A-13), one message event.</summary>
    public DomainResult<Message> AddAgentReply(Guid agentId, string? body, TimeProvider clock)
    {
        var added = AddMessage(AuthorType.Agent, agentId, MessageVisibility.Public, body, Actor.ForAgent(agentId), clock);
        if (added.IsFailure)
        {
            return added;
        }

        var now = DomainTime.Now(clock);
        FirstResponseAt ??= now;
        if (Status is TicketStatus.New or TicketStatus.Open && TicketStatusRules.CanTransition(Status, TicketStatus.Pending))
        {
            ApplyStatus(TicketStatus.Pending, Actor.ForAgent(agentId), now, clock);
        }

        return added;
    }

    /// <summary>An internal note: no status change, never visible to the customer.</summary>
    public DomainResult<Message> AddInternalNote(Guid agentId, string? body, TimeProvider clock) =>
        AddMessage(AuthorType.Agent, agentId, MessageVisibility.Internal, body, Actor.ForAgent(agentId), clock);

    /// <summary>A customer reply: a Pending or Solved ticket becomes Open (FR-TKT-12).</summary>
    public DomainResult<Message> AddCustomerReply(Guid requesterId, string? body, TimeProvider clock)
    {
        var added = AddMessage(AuthorType.Requester, requesterId, MessageVisibility.Public, body, Actor.ForRequester(requesterId), clock);
        if (added.IsFailure)
        {
            return added;
        }

        if (Status is TicketStatus.Pending or TicketStatus.Solved && TicketStatusRules.CanTransition(Status, TicketStatus.Open))
        {
            ApplyStatus(TicketStatus.Open, Actor.ForRequester(requesterId), DomainTime.Now(clock), clock);
        }

        return added;
    }

    public DomainResult Assign(Guid? agentId, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (AssigneeId == agentId)
        {
            return DomainResult.Ok();
        }

        var from = AssigneeId;
        AssigneeId = agentId;
        Raise(TicketEventType.Assigned, actor, Payload(("from", from), ("to", agentId)), clock);
        Touch(clock);
        return DomainResult.Ok();
    }

    public DomainResult ChangePriority(TicketPriority priority, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (Priority == priority)
        {
            return DomainResult.Ok();
        }

        var from = Priority;
        Priority = priority;
        Raise(TicketEventType.PriorityChanged, actor, Payload(("from", from.ToString()), ("to", priority.ToString())), clock);
        Touch(clock);
        return DomainResult.Ok();
    }

    /// <summary>Moves the ticket to another product. The number is unchanged (D-009).</summary>
    public DomainResult MoveToProduct(Guid productId, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (ProductId == productId)
        {
            return DomainResult.Ok();
        }

        var from = ProductId;
        ProductId = productId;
        Raise(TicketEventType.ProductChanged, actor, Payload(("from", from), ("to", productId)), clock);
        Touch(clock);
        return DomainResult.Ok();
    }

    public DomainResult AddTag(Guid tagId, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (_tagIds.Add(tagId))
        {
            Raise(TicketEventType.TagAdded, actor, Payload(("tagId", tagId)), clock);
            Touch(clock);
        }

        return DomainResult.Ok();
    }

    public DomainResult RemoveTag(Guid tagId, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (_tagIds.Remove(tagId))
        {
            Raise(TicketEventType.TagRemoved, actor, Payload(("tagId", tagId)), clock);
            Touch(clock);
        }

        return DomainResult.Ok();
    }

    /// <summary>Sets or clears the spam flag (D-024). The status is untouched.</summary>
    public DomainResult MarkSpam(bool isSpam, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        if (IsSpam == isSpam)
        {
            return DomainResult.Ok();
        }

        IsSpam = isSpam;
        Raise(TicketEventType.MarkedSpam, actor, Payload(("isSpam", isSpam)), clock);
        Touch(clock);
        return DomainResult.Ok();
    }

    /// <summary>
    /// The only way to continue a Closed ticket (D-008): a new ticket in the same product for the same requester, linked by
    /// <see cref="ParentTicketId"/>. This ticket's state is unchanged; it only gains a <c>FollowUpCreated</c> event.
    /// </summary>
    public DomainResult<Ticket> CreateFollowUp(TicketNumber number, TimeProvider clock)
    {
        if (Status != TicketStatus.Closed)
        {
            return DomainErrors.Conflict("ticket-not-closed", "Only a Closed ticket can have a follow-up.");
        }

        var followUp = CreateCore(number, ProductId, RequesterId, Subject, Channel, null, false, Id, clock);
        if (followUp.IsFailure)
        {
            return followUp;
        }

        // A FollowUpCreated stamp may sit past LastActivityAt: Raise stamps it without calling Touch. That is safe because a Closed
        // ticket only ever gains more FollowUpCreated events, so no later stamp can sort before it, and Restore seeds _lastStamp from
        // LastActivityAt only (a reload may therefore re-issue a stamp equal to an earlier follow-up stamp, which is harmless).
        Raise(TicketEventType.FollowUpCreated, Actor.ForRequester(RequesterId), Payload(("followUpTicketId", followUp.Value.Id)), clock);
        return followUp;
    }

    /// <summary>Called by the repository after it has staged the pending events and messages for persistence.</summary>
    public void AcceptChanges()
    {
        foreach (var message in _pendingMessages)
        {
            message.AcceptChanges();
        }

        _pendingEvents.Clear();
        _pendingMessages.Clear();
    }

    private static DomainResult<Ticket> CreateCore(
        TicketNumber number,
        Guid productId,
        Guid requesterId,
        string? subject,
        TicketChannel channel,
        string? metadataJson,
        bool metadataTrusted,
        Guid? parentTicketId,
        TimeProvider clock)
    {
        var title = Guard.RequiredText(subject, DomainLimits.SubjectMaxLength, "subject");
        if (title.IsFailure)
        {
            return title.Error!;
        }

        if (!IsValidJsonObject(metadataJson))
        {
            return DomainErrors.Validation("metadata-invalid", "Metadata must be a JSON object of at most 16000 characters.", "metadata");
        }

        var now = DomainTime.Now(clock);
        var ticket = new Ticket(
            EntityId.New(clock), number, productId, requesterId, title.Value, TicketStatus.New, TicketPriority.Normal, null, channel, false,
            parentTicketId, string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson, metadataTrusted, null, now, null, null, null, now, [], 0);

        ticket.Raise(
            TicketEventType.Created,
            Actor.ForRequester(requesterId),
            Payload(("number", number.ToString()), ("productId", productId), ("channel", channel.ToString()), ("parentTicketId", parentTicketId)),
            clock);
        ticket.Touch(clock);
        return DomainResult<Ticket>.Ok(ticket);
    }

    private static bool IsValidJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > DomainLimits.MetadataMaxLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Payload(params (string Key, object? Value)[] items) =>
        JsonSerializer.Serialize(items.Where(item => item.Value is not null).ToDictionary(item => item.Key, item => item.Value));

    private DomainError? EnsureWritable() =>
        TicketStatusRules.IsReadOnly(Status)
            ? DomainErrors.Conflict("ticket-closed", "A Closed ticket is read-only; continue it with a follow-up ticket.")
            : null;

    private DomainResult<Message> AddMessage(AuthorType authorType, Guid authorId, MessageVisibility visibility, string? body, Actor actor, TimeProvider clock)
    {
        if (EnsureWritable() is { } closed)
        {
            return closed;
        }

        var stamp = NextStamp(clock);
        var message = Message.CreateAt(Id, authorType, authorId, visibility, body, stamp, clock);
        if (message.IsFailure)
        {
            return message;
        }

        _lastStamp = stamp;
        _pendingMessages.Add(message.Value);
        Raise(TicketEventType.MessageAdded, actor, Payload(("messageId", message.Value.Id), ("visibility", visibility.ToString())), clock);
        Touch(clock);
        return message;
    }

    private void ApplyStatus(TicketStatus to, Actor actor, DateTimeOffset now, TimeProvider clock)
    {
        var from = Status;
        Status = to;
        if (to == TicketStatus.Solved)
        {
            SolvedAt = now;
        }
        else if (from == TicketStatus.Solved && to == TicketStatus.Open)
        {
            SolvedAt = null;
        }

        if (to == TicketStatus.Closed)
        {
            ClosedAt = now;
        }

        Raise(TicketEventType.StatusChanged, actor, Payload(("from", from.ToString()), ("to", to.ToString())), clock);
        Touch(clock);
    }

    /// <summary>Last activity is the read time at microsecond resolution, never earlier than a stamp already issued (so Restore can seed from it).</summary>
    private void Touch(TimeProvider clock)
    {
        var now = DomainTime.Now(clock);
        LastActivityAt = now > _lastStamp ? now : _lastStamp;
    }

    private void Raise(TicketEventType type, Actor actor, string payloadJson, TimeProvider clock) =>
        _pendingEvents.Add(TicketEvent.Raise(Id, type, actor, payloadJson, Stamp(clock), clock));

    /// <summary>
    /// A timestamp for the next event or message of this ticket, strictly later than the previous one (by one microsecond, the
    /// resolution of timestamptz) even when the clock has not moved, so a timeline sorted by time keeps the order things happened in.
    /// </summary>
    private DateTimeOffset Stamp(TimeProvider clock)
    {
        _lastStamp = NextStamp(clock);
        return _lastStamp;
    }

    /// <summary>The next stamp without consuming it: the clock truncated to whole microseconds, or one microsecond after the last stamp.</summary>
    private DateTimeOffset NextStamp(TimeProvider clock)
    {
        var now = DomainTime.Now(clock);
        var earliest = _lastStamp.AddTicks(DomainTime.MicrosecondTicks);
        return now > earliest ? now : earliest;
    }
}
