using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// The local-stack webhook capture switch redirects outbound deliveries, so it must not exist in a build that ships. A Release run of this project
/// checks the compiled assemblies; both configurations check what keeps it out (the source guard, the compile removal, and Release images).
/// </summary>
public sealed class WebhookCaptureAbsenceTests
{
    private const string CaptureNamespace = "TechStrap.Infrastructure.Webhooks.LocalCapture";

    private static string RepositoryRoot => ProjectGraph.FindRepositoryRoot();

    private static string InfrastructureDirectory => Path.Combine(RepositoryRoot, "src", "TechStrap.Infrastructure");

#if DEBUG
    [Fact]
    public void The_capture_switch_exists_in_a_Debug_build()
    {
        typeof(Infrastructure.InfrastructureAssemblyMarker).Assembly
            .GetType(CaptureNamespace + ".WebhookCaptureRegistration")
            .ShouldNotBeNull();
    }
#else
    [Fact]
    public void No_capture_type_exists_in_a_Release_build()
    {
        // The outbox staging class that was there first. It records deliveries to send; it captures nothing on the wire.
        var staging = typeof(Infrastructure.InfrastructureAssemblyMarker).Assembly.GetType("TechStrap.Infrastructure.Webhooks.WebhookCapture");
        staging.ShouldNotBeNull();

        var offenders = new[] { typeof(Infrastructure.InfrastructureAssemblyMarker).Assembly, typeof(Worker.Program).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !IsOrIsNestedIn(type, staging))
            .Where(type => type.Namespace?.Contains(".Webhooks.LocalCapture", StringComparison.Ordinal) == true
                || (type.FullName!.Contains(".Webhooks.", StringComparison.Ordinal) && type.Name.Contains("Capture", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsOrIsNestedIn(Type type, Type outer)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == outer)
            {
                return true;
            }
        }

        return false;
    }
#endif

    [Fact]
    public void Capture_source_is_debug_only()
    {
        var directory = Path.Combine(InfrastructureDirectory, "Webhooks", "LocalCapture");
        var sources = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories) : [];
        sources.ShouldNotBeEmpty();
        foreach (var source in sources)
        {
            var lines = File.ReadAllLines(source).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            lines[0].ShouldBe("#if DEBUG", source);
            lines[^1].ShouldBe("#endif", source);
        }

        var removals = XDocument.Load(Path.Combine(InfrastructureDirectory, "TechStrap.Infrastructure.csproj"))
            .Descendants("Compile")
            .Where(element => ((string?)element.Attribute("Remove"))?.Replace('\\', '/') == "Webhooks/LocalCapture/**")
            .ToList();
        removals.Count.ShouldBe(1);
        var condition = (string?)removals[0].Attribute("Condition") ?? (string?)removals[0].Parent!.Attribute("Condition");
        condition.ShouldBe("'$(Configuration)' != 'Debug'");

        var dockerfiles = Directory.GetFiles(RepositoryRoot, "Dockerfile.*");
        dockerfiles.ShouldNotBeEmpty();
        foreach (var dockerfile in dockerfiles)
        {
            var publishLines = File.ReadAllLines(dockerfile)
                .Where(line => !line.TrimStart().StartsWith('#') && line.Contains("dotnet publish", StringComparison.Ordinal))
                .ToList();
            publishLines.ShouldNotBeEmpty(dockerfile);
            publishLines.ShouldAllBe(line => Regex.IsMatch(line, @"\s-c Release(\s|$)"), dockerfile);
        }
    }
}
