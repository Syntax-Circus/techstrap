namespace TechStrap.Architecture.Tests;

/// <summary>
/// The allowed reference direction from docs/architecture/02-ARCHITECTURE.md section 2.
/// Evaluate() is a pure function so the tests can prove it fails on a deliberately bad graph.
/// </summary>
public static class ReferenceRules
{
    public const string Domain = "TechStrap.Domain";
    public const string Contracts = "TechStrap.Contracts";
    public const string Hosting = "TechStrap.Hosting";
    public const string Application = "TechStrap.Application";
    public const string Infrastructure = "TechStrap.Infrastructure";
    public const string Api = "TechStrap.Api";
    public const string Worker = "TechStrap.Worker";
    public const string Admin = "TechStrap.Admin";
    public const string Portal = "TechStrap.Portal";
    public const string Client = "TechStrap.Client";
    public const string ClientMaui = "TechStrap.Client.Maui";

    private const string CommonPackage = "SyntaxCircus.Common";

    public static IReadOnlyDictionary<string, string[]> AllowedProjectReferences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Domain] = [],
            [Contracts] = [],
            [Hosting] = [],
            [Application] = [Domain, Contracts],
            [Infrastructure] = [Application, Domain, Contracts],
            [Api] = [Application, Infrastructure, Contracts, Hosting],
            [Worker] = [Application, Infrastructure, Contracts, Hosting],
            [Admin] = [Contracts, Hosting],
            [Portal] = [Contracts, Hosting],
            [Client] = [Contracts],
            [ClientMaui] = [Client, Contracts],
        };

    /// <summary>Projects that must stay free of packages and framework references (leaf libraries).</summary>
    public static IReadOnlyCollection<string> DependencyFreeProjects { get; } = [Domain, Contracts];

    public static IReadOnlyList<string> Evaluate(IReadOnlyDictionary<string, ProjectNode> graph)
    {
        var violations = new List<string>();

        foreach (var project in graph.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!AllowedProjectReferences.TryGetValue(project.Name, out var allowed))
            {
                violations.Add($"{project.Name}: unknown project. Add it to ReferenceRules.AllowedProjectReferences.");
                continue;
            }

            foreach (var reference in project.ProjectReferences.Where(r => !allowed.Contains(r)).Order(StringComparer.Ordinal))
            {
                violations.Add($"{project.Name} must not reference {reference}.");
            }

            if (DependencyFreeProjects.Contains(project.Name))
            {
                foreach (var package in project.PackageReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{project.Name} must not reference package {package}.");
                }

                foreach (var framework in project.FrameworkReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{project.Name} must not reference framework {framework}.");
                }
            }

            if (project.Name == Application)
            {
                foreach (var package in project.PackageReferences.Where(p => p != CommonPackage).Order(StringComparer.Ordinal))
                {
                    violations.Add($"{Application} may reference only {CommonPackage}, not package {package}.");
                }

                foreach (var framework in project.FrameworkReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{Application} must not reference framework {framework}.");
                }
            }
        }

        foreach (var expected in AllowedProjectReferences.Keys.Where(name => !graph.ContainsKey(name)).Order(StringComparer.Ordinal))
        {
            violations.Add($"{expected}: project is missing from src/.");
        }

        return violations;
    }
}
