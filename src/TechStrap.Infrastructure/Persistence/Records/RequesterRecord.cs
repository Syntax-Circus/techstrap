namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class RequesterRecord
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ExternalUserRef { get; set; }

    public DateTimeOffset? ErasedAt { get; set; }
}
