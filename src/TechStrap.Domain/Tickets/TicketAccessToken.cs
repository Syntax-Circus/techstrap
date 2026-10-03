using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

/// <summary>
/// The customer's per-ticket access credential. Only a hash is stored (D-001 token scheme). Expiry slides: every valid use
/// moves it to now plus <see cref="Lifetime"/> (90 days, Assumption from the spec), but never past <see cref="MaxLifetime"/> from issue (D-032).
/// </summary>
public sealed class TicketAccessToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>Absolute lifetime from issue; sliding never passes it (D-032).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    private TicketAccessToken(Guid id, Guid ticketId, Guid requesterId, string tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt, DateTimeOffset? revokedAt, DateTimeOffset? lastUsedAt)
    {
        Id = id;
        TicketId = ticketId;
        RequesterId = requesterId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        LastUsedAt = lastUsedAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public Guid RequesterId { get; }

    public string TokenHash { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static DomainResult<TicketAccessToken> Issue(Guid ticketId, Guid requesterId, string? tokenHash, TimeProvider clock)
    {
        var hash = Guard.RequiredText(tokenHash, DomainLimits.HashMaxLength, "token-hash");
        var now = DomainTime.Now(clock);
        return hash.IsFailure
            ? hash.Error!
            : DomainResult<TicketAccessToken>.Ok(new TicketAccessToken(EntityId.New(clock), ticketId, requesterId, hash.Value, now, now + Lifetime, null, null));
    }

    public static TicketAccessToken Restore(Guid id, Guid ticketId, Guid requesterId, string tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt, DateTimeOffset? revokedAt, DateTimeOffset? lastUsedAt) =>
        new(id, ticketId, requesterId, tokenHash, issuedAt, expiresAt, revokedAt, lastUsedAt);

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(TimeProvider clock) => DomainTime.Now(clock) >= ExpiresAt;

    public bool IsValid(TimeProvider clock) => !IsRevoked && !IsExpired(clock);

    public void Revoke(TimeProvider clock) => RevokedAt ??= DomainTime.Now(clock);

    /// <summary>A valid use slides the expiry; a revoked or expired token cannot be used (callers answer with a uniform not-found).</summary>
    public DomainResult RecordUse(TimeProvider clock)
    {
        if (!IsValid(clock))
        {
            return DomainErrors.NotFound("access-token-invalid", "The access token is not valid.");
        }

        var now = DomainTime.Now(clock);
        LastUsedAt = now;
        var slid = now + Lifetime;
        var cap = IssuedAt + MaxLifetime;
        ExpiresAt = slid < cap ? slid : cap;
        return DomainResult.Ok();
    }
}
