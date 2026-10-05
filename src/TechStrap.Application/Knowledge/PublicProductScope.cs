using System.Text.RegularExpressions;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Knowledge;

/// <summary>Resolves a portal product key. Blank, unknown and inactive keys all give null, so the public KB never says which keys exist.</summary>
internal static partial class PublicProductScope
{
    public static async Task<Product?> ResolveAsync(IProductRepository products, string? productKey, CancellationToken cancellationToken)
    {
        if (!IsSlug(productKey, DomainLimits.SlugMaxLength))
        {
            return null;
        }

        var product = await products.GetByKeyAsync(productKey!.Trim(), cancellationToken);
        return product is { IsActive: true } ? product : null;
    }

    /// <summary>
    /// True when the trimmed value has the stored slug shape (lower-case letters, digits and single hyphens, within the limit; the shape of Guard.Slug).
    /// Anonymous callers pass route and query values, so anything else (a NUL, a lone surrogate, upper case) never reaches a query.
    /// </summary>
    public static bool IsSlug(string? value, int maxLength)
    {
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text) && text.Length <= maxLength && SlugShape().IsMatch(text);
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();
}
