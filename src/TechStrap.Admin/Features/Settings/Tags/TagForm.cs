using System.Text.RegularExpressions;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>The checks the tag form makes before it sends, with the server's own rules (the color with the same Contracts constant). The field names of a 400 are <see cref="Clients.ApiFields"/>.</summary>
internal static partial class TagForm
{
    private const int NameMaxLength = 50;
    private const int SlugMaxLength = 40;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    public static string? CheckName(string value) =>
        string.IsNullOrWhiteSpace(value) ? TagsCopy.NameRequired : value.Trim().Length > NameMaxLength ? TagsCopy.NameTooLong : null;

    public static string? CheckSlug(string value) =>
        string.IsNullOrWhiteSpace(value) ? TagsCopy.SlugRequired : value.Trim().Length > SlugMaxLength || !SlugPattern().IsMatch(value.Trim()) ? TagsCopy.SlugInvalid : null;

    public static string? CheckColour(string value) => Regex.IsMatch(value.Trim(), BrandingRules.ColourPattern) ? null : TagsCopy.ColourInvalid;

    /// <summary>A slug suggested from a name ("Billing issue" gives "billing-issue"). The agent can change it until the tag is created.</summary>
    public static string SlugFrom(string name)
    {
        var slug = NotSlugCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > SlugMaxLength ? slug[..SlugMaxLength].TrimEnd('-') : slug;
    }
}
