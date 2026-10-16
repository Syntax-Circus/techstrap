namespace TechStrap.Contracts.Features;

/// <summary>Supported feature values.</summary>
public static class FeaturePublicationStates
{
    /// <summary>A supported value.</summary>
    public const string Draft = "Draft";
    /// <summary>A supported value.</summary>
    public const string Published = "Published";
}

/// <summary>Supported feature values.</summary>
public static class FeatureProgressStatuses
{
    /// <summary>A supported value.</summary>
    public const string UnderConsideration = "UnderConsideration";
    /// <summary>A supported value.</summary>
    public const string Planned = "Planned";
    /// <summary>A supported value.</summary>
    public const string InProgress = "InProgress";
    /// <summary>A supported value.</summary>
    public const string Shipped = "Shipped";
    /// <summary>A supported value.</summary>
    public const string NotPlanned = "NotPlanned";
    /// <summary>Every supported progress status.</summary>
    public static IReadOnlyList<string> All { get; } = [UnderConsideration, Planned, InProgress, Shipped, NotPlanned];
}

/// <summary>The editable agent view. Version is an opaque concurrency token; product and slug are permanent.</summary>
public sealed record FeatureDto(
    Guid Id,
    Guid ProductId,
    string Slug,
    string Title,
    string DescriptionMarkdown,
    string PublicationState,
    string ProgressStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>One row in the private agent feature list, including drafts.</summary>
public sealed record FeatureSummaryDto(
    Guid Id,
    Guid ProductId,
    string Slug,
    string Title,
    string PublicationState,
    string ProgressStatus,
    DateTimeOffset UpdatedAt);

/// <summary>A published feature summary containing only explicitly curated content.</summary>
public sealed record PublicFeatureSummaryDto(Guid Id, string Slug, string Title, string ProgressStatus, DateTimeOffset UpdatedAt);

/// <summary>A published feature with sanitized HTML, without linked tickets or requester data.</summary>
public sealed record PublicFeatureDto(Guid Id, string Slug, string Title, string DescriptionHtml, string ProgressStatus, DateTimeOffset UpdatedAt);

/// <summary>Creates a draft under consideration. Product and slug cannot change after creation.</summary>
public sealed record CreateFeatureRequest(Guid ProductId, string? Slug, string? Title, string? DescriptionMarkdown);

/// <summary>Replaces curated content and progress. Version must match the latest feature read.</summary>
public sealed record UpdateFeatureRequest(string? Title, string? DescriptionMarkdown, string? ProgressStatus, uint Version);

/// <summary>Links a private request to one feature; null unlinks. RowVersion is required.</summary>
public sealed record LinkTicketFeatureRequest(Guid? FeatureId, uint? RowVersion);

/// <summary>One linked private request, available to agents only.</summary>
public sealed record LinkedFeatureTicketDto(Guid Id, string Number, string Subject, string Status, uint Version);
