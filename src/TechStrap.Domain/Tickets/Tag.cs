using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

/// <summary>A label agents put on tickets. The slug is unique and never changes.</summary>
public sealed class Tag
{
    private Tag(Guid id, string slug, string name, string colour)
    {
        Id = id;
        Slug = slug;
        Name = name;
        Colour = colour;
    }

    public Guid Id { get; }

    public string Slug { get; }

    public string Name { get; private set; }

    /// <summary>Stored as upper-case <c>#RRGGBB</c>.</summary>
    public string Colour { get; private set; }

    public static DomainResult<Tag> Create(string? slug, string? name, string? colour, TimeProvider clock)
    {
        var tagSlug = Guard.Slug(slug, DomainLimits.SlugMaxLength, "slug");
        var tagName = Guard.RequiredText(name, DomainLimits.TagNameMaxLength, "name");
        var tagColour = Guard.Colour(colour, "colour");

        return Guard.FirstError(tagSlug, tagName, tagColour) is { } error
            ? error
            : DomainResult<Tag>.Ok(new Tag(EntityId.New(clock), tagSlug.Value, tagName.Value, tagColour.Value));
    }

    public static Tag Restore(Guid id, string slug, string name, string colour) => new(id, slug, name, colour);

    public DomainResult Update(string? name, string? colour)
    {
        var tagName = Guard.RequiredText(name, DomainLimits.TagNameMaxLength, "name");
        var tagColour = Guard.Colour(colour, "colour");
        if (Guard.FirstError(tagName, tagColour) is { } error)
        {
            return error;
        }

        Name = tagName.Value;
        Colour = tagColour.Value;
        return DomainResult.Ok();
    }
}
