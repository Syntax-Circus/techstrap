using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Settings.Tags;

internal sealed record TagRowViewModel(Guid Id, string Slug, string Name, string Colour, int TicketCount)
{
    public static TagRowViewModel From(TagSummaryDto tag) => new(tag.Id, tag.Slug, tag.Name, tag.Colour, tag.TicketCount);

    /// <summary>The row after a rename or recolor: the API answers with the tag only, so the count the list already showed stays.</summary>
    public TagRowViewModel With(TagDto tag) => this with { Name = tag.Name, Colour = tag.Colour };
}
