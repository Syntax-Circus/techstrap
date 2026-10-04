using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>One row of the key list: the API sends only the label and the prefix, never the key, and this never holds more.</summary>
internal sealed record ApiKeyRowViewModel(Guid Id, string Kind, string KeyPrefix, string? Label, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt)
{
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>What the key is called in a sentence: its label, or its prefix when it has none.</summary>
    public string Name => string.IsNullOrWhiteSpace(Label) ? KeyPrefix : Label;

    public static ApiKeyRowViewModel From(ProductApiKeyDto key) =>
        new(key.Id, key.Kind, key.KeyPrefix, key.Label, key.CreatedAt, key.RevokedAt, key.LastUsedAt);
}
