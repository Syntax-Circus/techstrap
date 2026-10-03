using System.Text.Json;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Outbox;

public enum OutboxStatus
{
    Pending,
    Sending,
    Sent,
    DeadLettered,
    Discarded,
}

/// <summary>The retry schedule: exponential backoff from one minute, capped, and a dead letter after the fifth failed attempt.</summary>
public static class OutboxRetryPolicy
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    /// <summary>The wait after the given (1-based) failed attempt: 1, 2, 4, 8 minutes, then the cap.</summary>
    public static TimeSpan DelayAfter(int attempts)
    {
        var factor = Math.Pow(2, Math.Max(attempts, 1) - 1);
        var delay = TimeSpan.FromTicks((long)Math.Min(BaseDelay.Ticks * factor, MaxDelay.Ticks));
        return delay > MaxDelay ? MaxDelay : delay;
    }
}

/// <summary>
/// A queued outbound email (transactional outbox, D-010). <see cref="Attempts"/> counts delivery attempts started, so a claim
/// that expires (a crashed worker) still counts toward the dead-letter threshold. Delivery is at-least-once.
/// </summary>
public sealed class EmailOutboxItem
{
    private EmailOutboxItem(
        Guid id,
        string kind,
        string toAddress,
        string payloadJson,
        Guid? productId,
        Guid? ticketId,
        OutboxStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        string? claimedBy,
        DateTimeOffset? lockedUntil,
        string? lastError,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt)
    {
        Id = id;
        Kind = kind;
        ToAddress = toAddress;
        PayloadJson = payloadJson;
        ProductId = productId;
        TicketId = ticketId;
        Status = status;
        Attempts = attempts;
        NextAttemptAt = nextAttemptAt;
        ClaimedBy = claimedBy;
        LockedUntil = lockedUntil;
        LastError = lastError;
        CreatedAt = createdAt;
        SentAt = sentAt;
    }

    public Guid Id { get; }

    public string Kind { get; }

    public string ToAddress { get; }

    public string PayloadJson { get; }

    public Guid? ProductId { get; }

    public Guid? TicketId { get; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? ClaimedBy { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? SentAt { get; private set; }

    public static DomainResult<EmailOutboxItem> Enqueue(string? kind, string? toAddress, string? payloadJson, Guid? productId, Guid? ticketId, TimeProvider clock)
    {
        var itemKind = Guard.RequiredText(kind, DomainLimits.KindMaxLength, "kind");
        var address = Guard.Email(toAddress, "to-address");
        if (Guard.FirstError(itemKind, address) is { } error)
        {
            return error;
        }

        var payload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        if (!IsJsonObject(payload))
        {
            return DomainErrors.Validation("payload-invalid", "The payload must be a JSON object.", "payload");
        }

        var now = clock.GetUtcNow();
        return DomainResult<EmailOutboxItem>.Ok(new EmailOutboxItem(
            EntityId.New(clock), itemKind.Value, address.Value, payload, productId, ticketId, OutboxStatus.Pending, 0, now, null, null, null, now, null));
    }

    public static EmailOutboxItem Restore(
        Guid id,
        string kind,
        string toAddress,
        string payloadJson,
        Guid? productId,
        Guid? ticketId,
        OutboxStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        string? claimedBy,
        DateTimeOffset? lockedUntil,
        string? lastError,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt) =>
        new(id, kind, toAddress, payloadJson, productId, ticketId, status, attempts, nextAttemptAt, claimedBy, lockedUntil, lastError, createdAt, sentAt);

    /// <summary>Pending and due, or Sending with an expired claim.</summary>
    public bool IsClaimable(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return (Status == OutboxStatus.Pending && NextAttemptAt <= now)
            || (Status == OutboxStatus.Sending && LockedUntil <= now);
    }

    public DomainResult Claim(string? workerId, TimeSpan lease, TimeProvider clock)
    {
        var worker = Guard.RequiredText(workerId, DomainLimits.NameMaxLength, "worker-id");
        if (worker.IsFailure)
        {
            return worker.Error!;
        }

        if (!IsClaimable(clock))
        {
            return DomainErrors.Conflict("outbox-not-claimable", "The email is not due or is claimed by another worker.");
        }

        Status = OutboxStatus.Sending;
        Attempts++;
        ClaimedBy = worker.Value;
        LockedUntil = clock.GetUtcNow() + lease;
        return DomainResult.Ok();
    }

    public DomainResult MarkSent(TimeProvider clock)
    {
        if (Status != OutboxStatus.Sending)
        {
            return DomainErrors.Conflict("outbox-not-sending", "Only a claimed email can be marked sent.");
        }

        Status = OutboxStatus.Sent;
        SentAt = clock.GetUtcNow();
        ReleaseClaim();
        return DomainResult.Ok();
    }

    /// <summary>Back to Pending with the next backoff delay, or DeadLettered once <see cref="OutboxRetryPolicy.MaxAttempts"/> is reached.</summary>
    public DomainResult MarkFailed(string? error, TimeProvider clock)
    {
        if (Status != OutboxStatus.Sending)
        {
            return DomainErrors.Conflict("outbox-not-sending", "Only a claimed email can fail.");
        }

        var text = error?.Trim() ?? string.Empty;
        LastError = text.Length > DomainLimits.ErrorMaxLength ? text[..DomainLimits.ErrorMaxLength] : text;
        ReleaseClaim();
        if (Attempts >= OutboxRetryPolicy.MaxAttempts)
        {
            Status = OutboxStatus.DeadLettered;
        }
        else
        {
            Status = OutboxStatus.Pending;
            NextAttemptAt = clock.GetUtcNow() + OutboxRetryPolicy.DelayAfter(Attempts);
        }

        return DomainResult.Ok();
    }

    /// <summary>An admin puts a dead letter back in the queue with a fresh attempt count (D-022).</summary>
    public DomainResult Retry(TimeProvider clock)
    {
        if (Status != OutboxStatus.DeadLettered)
        {
            return DomainErrors.Conflict("outbox-not-dead-lettered", "Only a dead-lettered email can be retried.");
        }

        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAt = clock.GetUtcNow();
        return DomainResult.Ok();
    }

    public DomainResult Discard()
    {
        if (Status != OutboxStatus.DeadLettered)
        {
            return DomainErrors.Conflict("outbox-not-dead-lettered", "Only a dead-lettered email can be discarded.");
        }

        Status = OutboxStatus.Discarded;
        return DomainResult.Ok();
    }

    private static bool IsJsonObject(string json)
    {
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

    private void ReleaseClaim()
    {
        ClaimedBy = null;
        LockedUntil = null;
    }
}
