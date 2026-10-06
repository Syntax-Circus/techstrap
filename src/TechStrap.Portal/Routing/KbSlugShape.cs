using System.Text.RegularExpressions;

namespace TechStrap.Portal.Routing;

/// <summary>
/// The shape of a knowledge-base category or article slug: the same lowercase letters, digits and single hyphens as a product key, at most 80 characters (the Domain's <c>DomainLimits.KbSlugMaxLength</c>; the Portal
/// cannot reference the Domain). A slug that does not have it is never sent to the API, so a crafted route segment cannot reach a request path, and it is answered as an unknown page.
/// </summary>
public static partial class KbSlugShape
{
    /// <summary>The Domain's <c>DomainLimits.KbSlugMaxLength</c>.</summary>
    public const int MaxLength = 80;

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    public static bool IsWellFormed(string? slug) => slug is { Length: > 0 and <= MaxLength } && Slug().IsMatch(slug);
}
