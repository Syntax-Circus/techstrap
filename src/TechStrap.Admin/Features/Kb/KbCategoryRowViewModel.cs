using System.Globalization;
using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>One category row, ready to draw. <see cref="Version"/> is the one the list was read with: the update sends it, so a stale save is a 409 and never an overwrite.</summary>
internal sealed record KbCategoryRowViewModel(Guid Id, Guid? ProductId, string ProductName, string Slug, string Name, string? Description, int SortOrder, uint Version)
{
    public static KbCategoryRowViewModel From(KbCategoryDto category, IReadOnlyList<ProductDto> products) => new(
        category.Id,
        category.ProductId,
        category.ProductId is { } id ? products.FirstOrDefault(p => p.Id == id)?.Name ?? KbCategoriesCopy.UnknownProduct : KbCategoriesCopy.Shared,
        category.Slug,
        category.Name,
        category.Description,
        category.SortOrder,
        category.Version);

    /// <summary>Rows in sort order, then by name, as the API lists them (a created or renamed row is placed the same way before the next read).</summary>
    public static IReadOnlyList<KbCategoryRowViewModel> Sorted(IEnumerable<KbCategoryRowViewModel> rows) =>
        [.. rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Slug, StringComparer.Ordinal)];
}

/// <summary>The checks the category form makes before it sends, with the server's own rules. The field names of a 400 are <see cref="ApiFields"/>.</summary>
internal static partial class KbCategoryForm
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 300;
    public const int SortOrderStep = 10;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    public static string? CheckName(string value) =>
        string.IsNullOrWhiteSpace(value) ? KbCategoriesCopy.NameRequired : value.Trim().Length > NameMaxLength ? KbCategoriesCopy.NameTooLong : null;

    public static string? CheckSlug(string value)
    {
        var slug = value.Trim();
        return string.IsNullOrEmpty(slug) ? KbCategoriesCopy.SlugRequired
            : slug.Length > KbEditorLimits.SlugMaxLength || !SlugPattern().IsMatch(slug) ? KbCategoriesCopy.SlugInvalid
            : slug == KbLimits.ReservedCategorySlug ? KbCategoriesCopy.SlugReserved : null;
    }

    public static string? CheckDescription(string value) => value.Trim().Length > DescriptionMaxLength ? KbCategoriesCopy.DescriptionTooLong : null;

    public static string? CheckSortOrder(string value) => TryParseSortOrder(value, out _) ? null : KbCategoriesCopy.SortOrderInvalid;

    public static bool TryParseSortOrder(string value, out int sortOrder) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sortOrder);

    /// <summary>A slug suggested from a name ("Getting started" gives "getting-started"). The agent can change it until the category is created.</summary>
    public static string SlugFrom(string name)
    {
        var slug = NotSlugCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > KbEditorLimits.SlugMaxLength ? slug[..KbEditorLimits.SlugMaxLength].TrimEnd('-') : slug;
    }

    /// <summary>The sort order a new category starts with: the step after the highest in the list, so it comes last until the agent says otherwise.</summary>
    public static int NextSortOrder(IEnumerable<KbCategoryRowViewModel> rows) => rows.Select(r => r.SortOrder).DefaultIfEmpty(0).Max() + SortOrderStep;
}
