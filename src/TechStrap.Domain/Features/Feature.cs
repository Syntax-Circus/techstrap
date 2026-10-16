using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Features;

public enum FeaturePublicationState
{
    Draft,
    Published,
}
public enum FeatureProgressStatus
{
    UnderConsideration,
    Planned,
    InProgress,
    Shipped,
    NotPlanned,
}
/// <summary>A product-owned, explicitly curated public feature. Private requests are never its public content.</summary>
public sealed class Feature
{
    private Feature(
        Guid id,
        Guid productId,
        string slug,
        string title,
        string descriptionMarkdown,
        FeaturePublicationState publicationState,
        FeatureProgressStatus progressStatus,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        uint version)
    {
        Id = id;
        ProductId = productId;
        Slug = slug;
        Title = title;
        DescriptionMarkdown = descriptionMarkdown;
        PublicationState = publicationState;
        ProgressStatus = progressStatus;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Version = version;
    }
    public Guid Id { get; }
    public Guid ProductId { get; }
    public string Slug { get; }
    public string Title { get; private set; }
    public string DescriptionMarkdown { get; private set; }

    public FeaturePublicationState PublicationState { get; private set; }

    public FeatureProgressStatus ProgressStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; }

    public static DomainResult<Feature> Create(Guid productId, string? slug, string? title, string? descriptionMarkdown, TimeProvider clock)
    {
        var s = Guard.Slug(slug, DomainLimits.KbSlugMaxLength, "slug");
        var t = Guard.RequiredText(title, DomainLimits.KbTitleMaxLength, "title");
        var d = Guard.RequiredText(descriptionMarkdown, DomainLimits.KbBodyMaxLength, "descriptionMarkdown");
        if (Guard.FirstError(s, t, d) is { } e)
        {
            return e;
        }
        if (productId == Guid.Empty)
        {
            return DomainErrors.Validation("product-required", "Choose a product.", "productId");
        }
        var now = DomainTime.Now(clock);
        return DomainResult<Feature>.Ok(new(EntityId.New(clock), productId, s.Value, t.Value, d.Value, FeaturePublicationState.Draft, FeatureProgressStatus.UnderConsideration, now, now, 0));
    }

    public static Feature Restore(
        Guid id,
        Guid productId,
        string slug,
        string title,
        string descriptionMarkdown,
        FeaturePublicationState publicationState,
        FeatureProgressStatus progressStatus,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        uint version) =>
        new(id, productId, slug, title, descriptionMarkdown, publicationState, progressStatus, createdAt, updatedAt, version);

    public DomainResult Update(string? title, string? descriptionMarkdown, FeatureProgressStatus progressStatus, TimeProvider clock)
    {
        var t = Guard.RequiredText(title, DomainLimits.KbTitleMaxLength, "title");
        var d = Guard.RequiredText(descriptionMarkdown, DomainLimits.KbBodyMaxLength, "descriptionMarkdown");
        if (Guard.FirstError(t, d) is { } e)
        {
            return e;
        }
        if (!Enum.IsDefined(progressStatus))
        {
            return DomainErrors.Validation("status-invalid", "Choose a valid feature progress status.", "progressStatus");
        }
        Title = t.Value;
        DescriptionMarkdown = d.Value;
        ProgressStatus = progressStatus;
        UpdatedAt = DomainTime.Now(clock);
        return DomainResult.Ok();
    }

    public void SetPublished(bool published, TimeProvider clock)
    {
        PublicationState = published ? FeaturePublicationState.Published : FeaturePublicationState.Draft;
        UpdatedAt = DomainTime.Now(clock);
    }
}
