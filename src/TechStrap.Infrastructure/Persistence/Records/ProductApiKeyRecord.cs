using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class ProductApiKeyRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public ApiKeyKind Kind { get; set; }

    public string KeyHash { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = string.Empty;

    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
