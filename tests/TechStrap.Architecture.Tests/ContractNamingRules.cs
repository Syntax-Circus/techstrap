namespace TechStrap.Architecture.Tests;

/// <summary>
/// Every public, non-static type in TechStrap.Contracts ends in Dto, Request or Response, so API shapes are recognizable
/// wherever they are used. Static classes hold shared constants and helpers. The named exemptions are
/// ProductAccentColors, a value tuple returned by the PHASE-02 ProductAccent helper (D-025), and the Contracts.Skins value types (D-053),
/// none of which is a top-level API shape.
/// </summary>
public static class ContractNamingRules
{
    private static readonly string[] Suffixes = ["Dto", "Request", "Response"];

    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
    {
        "TechStrap.Contracts.Branding.ProductAccentColors",

        // The skin engine's value types (D-053): ProductSkin is the nested value of the product DTOs and requests, the rest are the resolver's
        // inputs and outputs and the pack data. None is a top-level API shape.
        "TechStrap.Contracts.Skins.ProductSkin",
        "TechStrap.Contracts.Skins.ResolvedSkin",
        "TechStrap.Contracts.Skins.SkinResolution",
        "TechStrap.Contracts.Skins.SkinProblem",
        "TechStrap.Contracts.Skins.SkinTokens",
        "TechStrap.Contracts.Skins.SkinPack",
    };

    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> types) =>
        types
            .Where(type => type.IsPublic && !(type.IsAbstract && type.IsSealed))
            .Where(type => type.FullName is not null && !Exempt.Contains(type.FullName))
            .Where(type => !Suffixes.Any(suffix => BaseName(type).EndsWith(suffix, StringComparison.Ordinal)))
            .Select(type => $"{type.FullName} must end in Dto, Request or Response")
            .ToList();

    private static string BaseName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
