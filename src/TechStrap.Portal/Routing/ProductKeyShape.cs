using System.Text.RegularExpressions;

namespace TechStrap.Portal.Routing;

/// <summary>
/// The shape of a product key: lowercase ASCII letters and digits in groups joined by single hyphens, at most 40 characters. It is the Domain's slug rule (the Portal cannot reference the Domain),
/// and it is the one test every key passes before it is sent anywhere: a malformed key from a URL is answered as an unknown route without calling the API, so a crafted segment can never
/// reach a request path or a log line.
/// </summary>
public static partial class ProductKeyShape
{
    /// <summary>The Domain's <c>DomainLimits.SlugMaxLength</c>.</summary>
    public const int MaxLength = 40;

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    public static bool IsWellFormed(string? key) => key is { Length: > 0 and <= MaxLength } && Slug().IsMatch(key);
}
