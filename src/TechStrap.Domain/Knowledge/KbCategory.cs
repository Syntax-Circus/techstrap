using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Knowledge;

/// <summary>A single-level knowledge-base category, product-scoped or shared (null product).</summary>
public sealed class KbCategory
{
    private KbCategory(Guid id, Guid? productId, string name, string slug, int sortOrder)
    {
        Id = id;
        ProductId = productId;
        Name = name;
        Slug = slug;
        SortOrder = sortOrder;
    }

    public Guid Id { get; }

    /// <summary>Null means shared across all products.</summary>
    public Guid? ProductId { get; }

    public string Name { get; private set; }

    public string Slug { get; }

    public int SortOrder { get; private set; }

    public bool IsShared => ProductId is null;

    public static DomainResult<KbCategory> Create(Guid? productId, string? slug, string? name, int sortOrder, TimeProvider clock)
    {
        var categorySlug = Guard.Slug(slug, DomainLimits.KbSlugMaxLength, "slug");
        var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");

        return Guard.FirstError(categorySlug, categoryName) is { } error
            ? error
            : DomainResult<KbCategory>.Ok(new KbCategory(EntityId.New(clock), productId, categoryName.Value, categorySlug.Value, sortOrder));
    }

    public static KbCategory Restore(Guid id, Guid? productId, string name, string slug, int sortOrder) =>
        new(id, productId, name, slug, sortOrder);

    public DomainResult Update(string? name, int sortOrder)
    {
        var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
        if (categoryName.IsFailure)
        {
            return categoryName.Error!;
        }

        Name = categoryName.Value;
        SortOrder = sortOrder;
        return DomainResult.Ok();
    }
}
