using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Knowledge;

/// <summary>
/// A single-level knowledge-base category, product-scoped or shared (null product). The slug and the product never change. The slug
/// <c>search</c> is reserved for the portal's KB search page (D-044).
/// </summary>
public sealed class KbCategory
{
    private KbCategory(Guid id, Guid? productId, string name, string slug, string? description, int sortOrder, uint version)
    {
        Id = id;
        ProductId = productId;
        Name = name;
        Slug = slug;
        Description = description;
        SortOrder = sortOrder;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>Null means shared across all products.</summary>
    public Guid? ProductId { get; }

    public string Name { get; private set; }

    public string Slug { get; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>).</summary>
    public uint Version { get; }

    public bool IsShared => ProductId is null;

    public static DomainResult<KbCategory> Create(Guid? productId, string? slug, string? name, int sortOrder, TimeProvider clock, string? description = null)
    {
        var categorySlug = Guard.Slug(slug, DomainLimits.KbSlugMaxLength, "slug");
        var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
        var categoryDescription = Guard.OptionalText(description, DomainLimits.KbCategoryDescriptionMaxLength, "description");
        if (Guard.FirstError(categorySlug, categoryName, categoryDescription) is { } error)
        {
            return error;
        }

        if (categorySlug.Value == DomainLimits.KbReservedCategorySlug)
        {
            return DomainErrors.Validation("kb-category-reserved-slug", "The slug \"search\" is reserved for the knowledge-base search page.", "slug");
        }

        return DomainResult<KbCategory>.Ok(
            new KbCategory(EntityId.New(clock), productId, categoryName.Value, categorySlug.Value, categoryDescription.Value, sortOrder, 0));
    }

    public static KbCategory Restore(Guid id, Guid? productId, string name, string slug, string? description, int sortOrder, uint version) =>
        new(id, productId, name, slug, description, sortOrder, version);

    public DomainResult Update(string? name, string? description, int sortOrder)
    {
        var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
        var categoryDescription = Guard.OptionalText(description, DomainLimits.KbCategoryDescriptionMaxLength, "description");
        if (Guard.FirstError(categoryName, categoryDescription) is { } error)
        {
            return error;
        }

        Name = categoryName.Value;
        Description = categoryDescription.Value;
        SortOrder = sortOrder;
        return DomainResult.Ok();
    }
}
