using SyntaxCircus.Common;
using TechStrap.Contracts.Tags;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tags;

internal static class TagMapping
{
    public static TagDto ToDto(Tag tag) => new(tag.Id, tag.Slug, tag.Name, tag.Colour);
}

internal static class TagErrors
{
    public static ResultError NotFound() => new("tag-not-found", "That tag does not exist.", ResultErrorKind.NotFound);

    public static ResultError SlugTaken() => new("tag-slug-taken", "Another tag already uses this slug. Choose a different one.", ResultErrorKind.Conflict);

    public static ResultError InUse(int ticketCount) =>
        new("tag-in-use", $"This tag is on {ticketCount} tickets. Delete it with force to remove it from them first.", ResultErrorKind.Conflict);
}
