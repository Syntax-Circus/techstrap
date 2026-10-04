namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>The copy of the tags page: create, rename and recolour, and the two delete confirmations (an unused tag, and a tag in use with its count and a typed name).</summary>
public static class TagsCopy
{
    public const string DefaultColour = "#4B5563";

    public const string Heading = "Tags";
    public const string Loading = "Loading tags";
    public const string LoadFailed = "Couldn't load the tags.";
    public const string NoTags = "No tags yet";
    public const string NoTagsHint = "Create a tag, then agents can add it to tickets.";

    public const string NewHeading = "New tag";
    public const string NameLabel = "Name";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It can't be changed once the tag exists.";
    public const string ColourLabel = "Colour";
    public const string ColourHelp = "A # and six hex digits, such as #1D4ED8.";
    public const string Create = "Create tag";
    public const string Creating = "Creating\u2026";

    public const string ColumnTag = "Tag";
    public const string ColumnSlug = "Slug";
    public const string ColumnTickets = "Tickets";
    public const string ColumnActions = "Actions";
    public const string Edit = "Edit";
    public const string Save = "Save";
    public const string Cancel = "Cancel";
    public const string Delete = "Delete";
    public const string Reload = "Reload list";

    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 50 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 40 characters.";
    public const string SlugTaken = "Another tag already uses this slug.";
    public const string ColourInvalid = "Use a colour like #1D4ED8: a # and six hex digits.";

    public const string DeleteConfirm = "Delete tag";
    public const string DeleteUnusedBody = "No tickets use this tag. This can't be undone.";
    public const string DeleteUncertain = "The delete may have gone through. Reload the list to check before you try again.";
    public const string NowInUse = "This tag was just added to tickets. The count above is updated: type the name to delete it anyway.";
    public const string TagGone = "That tag no longer exists.";
    public const string SaveUncertain = "The change may have gone through. Reload the list to check before you try again.";

    public static string Tickets(int count) => count == 1 ? "1 ticket" : $"{count} tickets";

    public static string DeleteTitle(string name) => $"Delete the tag {name}?";

    public static string DeleteInUseBody(int count) =>
        $"This tag is on {Tickets(count)}. Deleting it removes it from all of them, and each ticket records the change. This can't be undone.";

    public static string DeleteFailed(string reason) => $"Couldn't delete the tag. Nothing was changed. {reason}";

    public static string Created(string name) => $"Created the tag {name}";

    public static string Saved(string name) => $"Saved the tag {name}";

    public static string Deleted(string name) => $"Deleted the tag {name}";

    public static string DeletedFromTickets(string name, int count) => $"Deleted the tag {name} and removed it from {Tickets(count)}";
}
