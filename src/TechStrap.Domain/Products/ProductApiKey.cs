using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Products;

/// <summary>Trusted keys are server-side and may set trusted metadata; Public keys are create-only and untrusted (D-001).</summary>
public enum ApiKeyKind
{
    Trusted,
    Public,
}

/// <summary>A product API key. Only the prefix and the opaque hash are stored; the hash algorithm belongs to PHASE-04.</summary>
public sealed class ProductApiKey
{
    private ProductApiKey(
        Guid id,
        Guid productId,
        ApiKeyKind kind,
        string keyPrefix,
        string keyHash,
        string? label,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset? lastUsedAt)
    {
        Id = id;
        ProductId = productId;
        Kind = kind;
        KeyPrefix = keyPrefix;
        KeyHash = keyHash;
        Label = label;
        CreatedAt = createdAt;
        RevokedAt = revokedAt;
        LastUsedAt = lastUsedAt;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    public ApiKeyKind Kind { get; }

    public string KeyPrefix { get; }

    public string KeyHash { get; }

    public string? Label { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Only a Trusted key may set trusted metadata and an external user reference.</summary>
    public bool TrustsMetadata => Kind == ApiKeyKind.Trusted;

    public static DomainResult<ProductApiKey> Create(
        Guid productId,
        ApiKeyKind kind,
        string? keyPrefix,
        string? keyHash,
        string? label,
        TimeProvider clock)
    {
        var prefix = Guard.RequiredText(keyPrefix, DomainLimits.KeyPrefixMaxLength, "key-prefix");
        var hash = Guard.RequiredText(keyHash, DomainLimits.HashMaxLength, "key-hash");
        var keyLabel = Guard.OptionalText(label, DomainLimits.LabelMaxLength, "label");
        if (Guard.FirstError(prefix, hash, keyLabel) is { } error)
        {
            return error;
        }

        if (prefix.Value.Length < DomainLimits.KeyPrefixMinLength)
        {
            return DomainErrors.Validation("key-prefix-too-short", $"A key prefix has at least {DomainLimits.KeyPrefixMinLength} characters.", "key-prefix");
        }

        return DomainResult<ProductApiKey>.Ok(new ProductApiKey(
            EntityId.New(clock), productId, kind, prefix.Value, hash.Value, keyLabel.Value, clock.GetUtcNow(), null, null));
    }

    public static ProductApiKey Restore(
        Guid id,
        Guid productId,
        ApiKeyKind kind,
        string keyPrefix,
        string keyHash,
        string? label,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset? lastUsedAt) =>
        new(id, productId, kind, keyPrefix, keyHash, label, createdAt, revokedAt, lastUsedAt);

    /// <summary>Revoking twice keeps the first revocation time.</summary>
    public void Revoke(TimeProvider clock) => RevokedAt ??= clock.GetUtcNow();

    /// <summary>A revoked key can never authenticate again, so recording its use is a conflict.</summary>
    public DomainResult RecordUse(TimeProvider clock)
    {
        if (IsRevoked)
        {
            return DomainErrors.Conflict("api-key-revoked", "The API key has been revoked.");
        }

        LastUsedAt = clock.GetUtcNow();
        return DomainResult.Ok();
    }
}
