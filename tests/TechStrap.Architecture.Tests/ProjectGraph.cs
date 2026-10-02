using System.Xml.Linq;

namespace TechStrap.Architecture.Tests;

/// <summary>One src project as declared in its .csproj: direct project, package and framework references.</summary>
public sealed record ProjectNode(
    string Name,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences,
    IReadOnlySet<string> FrameworkReferences);

public static class ProjectGraph
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("TechStrap.slnx was not found above " + AppContext.BaseDirectory);
    }

    public static IReadOnlyDictionary<string, ProjectNode> LoadSourceProjects(string repositoryRoot)
    {
        var projects = Directory
            .GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Parse)
            .ToList();

        return projects.ToDictionary(project => project.Name, StringComparer.Ordinal);
    }

    public static ProjectNode Parse(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToHashSet(StringComparer.Ordinal);
        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var frameworkReferences = document
            .Descendants("FrameworkReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        return new ProjectNode(
            Path.GetFileNameWithoutExtension(csprojPath),
            projectReferences,
            packageReferences,
            frameworkReferences);
    }
}
