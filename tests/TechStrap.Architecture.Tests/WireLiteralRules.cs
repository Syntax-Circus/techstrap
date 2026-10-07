using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// The wire names (credential headers and the intake route) are typed once, in Contracts, and shared by the API, the Portal and the SDK.
/// A quoted copy anywhere else in src/ is one rename away from a silent client break. Hosting references no TechStrap project, so its Sentry
/// header scrubber keeps its own copy and is a named exemption. Comment lines are ignored. A source scan: coarse on purpose.
/// </summary>
public static partial class WireLiteralRules
{
    public static IReadOnlyList<string> Literals { get; } = ["X-Api-Key", "X-Ticket-Token", "Idempotency-Key", "api/intake/tickets"];

    /// <summary>Repository-relative paths (forward slashes) that may hold the literals.</summary>
    public static IReadOnlyList<string> AllowedPaths { get; } =
    [
        "src/TechStrap.Contracts/Http/HeaderNames.cs",
        "src/TechStrap.Contracts/Intake/IntakeRoutes.cs",
        "src/TechStrap.Hosting/Sentry/SensitiveHeaderSentryProcessor.cs",
    ];

    [GeneratedRegex("^\\s*//.*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex CommentLine();

    public static IReadOnlyList<string> Evaluate(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        foreach (var (path, source) in files)
        {
            var normalized = path.Replace('\\', '/');
            if (AllowedPaths.Any(allowed => normalized.EndsWith(allowed, StringComparison.Ordinal)))
            {
                continue;
            }

            var text = CommentLine().Replace(source, string.Empty);
            foreach (var literal in Literals.Where(literal => text.Contains("\"" + literal + "\"", StringComparison.Ordinal)))
            {
                violations.Add($"{normalized} contains the wire literal \"{literal}\". Use the Contracts constant (HeaderNames or IntakeRoutes).");
            }
        }

        return violations;
    }

    /// <summary>
    /// eng/Packaging.props is imported by every packable project, so a package it references would become a dependency of the shipped
    /// package (and break the dependency-free Contracts). Any reference there must be a build-only asset.
    /// </summary>
    public static IReadOnlyList<string> PackagingReferenceViolations(XDocument props) =>
        props.Descendants("PackageReference")
            .Where(reference => !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase))
            .Select(reference => $"eng/Packaging.props: PackageReference {(string?)reference.Attribute("Include")} must set PrivateAssets=\"all\".")
            .ToList();

    public static IEnumerable<(string Path, string Text)> SourceFiles(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "src");
        var skipped = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" };
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !skipped.Any(file.Contains))
            .Select(file => (Path.GetRelativePath(repositoryRoot, file), File.ReadAllText(file)));
    }
}
