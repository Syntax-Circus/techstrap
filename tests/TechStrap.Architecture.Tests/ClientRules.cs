namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the Client SDK (PHASE-11, D-047), modeled on <see cref="PortalRules"/>. TechStrap.Client ships as a public NuGet package, so what it references is what every consumer restores:
/// the Contracts project, a short list of reviewed packages and no framework reference. Pure over a parsed project so the tests can feed it a bad one.
/// </summary>
public static class ClientRules
{
    /// <summary>The only packages the Client project may reference. A new package becomes a dependency of every consumer: add it here in the same commit and argue for it.</summary>
    public static IReadOnlySet<string> AllowedPackages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Options",
        "SyntaxCircus.Common",
        "SyntaxCircus.Http.Resilience",
    };

    /// <summary>The only project the Client may reference (D-005): no Application, Infrastructure, Domain or host.</summary>
    public static IReadOnlySet<string> AllowedProjects { get; } = new HashSet<string>(StringComparer.Ordinal) { ReferenceRules.Contracts };

    public static IReadOnlyList<string> Violations(ProjectNode client)
    {
        var violations = new List<string>();

        violations.AddRange(client.PackageReferences
            .Where(package => !AllowedPackages.Contains(package))
            .Order(StringComparer.Ordinal)
            .Select(package => $"{client.Name} must not reference package {package}. It is a dependency of every consumer; add it to ClientRules.AllowedPackages if it is a reviewed choice."));
        violations.AddRange(client.ProjectReferences
            .Where(project => !AllowedProjects.Contains(project))
            .Order(StringComparer.Ordinal)
            .Select(project => $"{client.Name} must not reference project {project}. It may reference only {ReferenceRules.Contracts}."));
        violations.AddRange(client.FrameworkReferences
            .Order(StringComparer.Ordinal)
            .Select(framework => $"{client.Name} must not reference framework {framework}. It must stay usable from any .NET app."));

        return violations;
    }
}
