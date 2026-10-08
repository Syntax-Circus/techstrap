namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the MAUI helper (PHASE-11b, D-048), modelled on <see cref="ClientRules"/>. TechStrap.Client.Maui is a plain net10.0 library that reads MAUI Essentials interfaces, so it needs no workload:
/// a short list of reviewed packages, the Client and Contracts projects, no framework reference and never UseMaui. Pure over a parsed project so the tests can feed it a bad one.
/// </summary>
public static class ClientMauiRules
{
    /// <summary>The only packages the Client.Maui project may reference. A new package becomes a dependency of every consumer: add it here in the same commit and argue for it.</summary>
    public static IReadOnlySet<string> AllowedPackages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Maui.Essentials",
    };

    /// <summary>The only projects the Client.Maui project may reference.</summary>
    public static IReadOnlySet<string> AllowedProjects { get; } = new HashSet<string>(StringComparer.Ordinal) { ReferenceRules.Client, ReferenceRules.Contracts };

    /// <summary>True when the project file text sets the UseMaui property (the graph reads references only, not properties).</summary>
    public static bool SetsUseMaui(string projectFileText) =>
        projectFileText.Contains("<UseMaui>", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Violations(ProjectNode project, string projectFileText)
    {
        var violations = new List<string>();

        violations.AddRange(project.PackageReferences
            .Where(package => !AllowedPackages.Contains(package))
            .Order(StringComparer.Ordinal)
            .Select(package => $"{project.Name} must not reference package {package}. It is a dependency of every consumer; add it to ClientMauiRules.AllowedPackages if it is a reviewed choice."));
        violations.AddRange(project.ProjectReferences
            .Where(reference => !AllowedProjects.Contains(reference))
            .Order(StringComparer.Ordinal)
            .Select(reference => $"{project.Name} must reference only {ReferenceRules.Client} and {ReferenceRules.Contracts}, not {reference}."));
        violations.AddRange(project.FrameworkReferences
            .Order(StringComparer.Ordinal)
            .Select(framework => $"{project.Name} must not reference a framework ({framework}). It stays a plain net10.0 library."));

        if (SetsUseMaui(projectFileText))
        {
            violations.Add($"{project.Name} must not set UseMaui. It reads MAUI Essentials interfaces on plain net10.0 and needs no workload (D-048).");
        }

        return violations;
    }
}