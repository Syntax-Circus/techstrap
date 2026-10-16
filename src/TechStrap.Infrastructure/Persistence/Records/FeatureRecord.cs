using TechStrap.Domain.Features;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class FeatureRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string DescriptionMarkdown { get; set; } = string.Empty;

    public FeaturePublicationState PublicationState { get; set; }

    public FeatureProgressStatus ProgressStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
