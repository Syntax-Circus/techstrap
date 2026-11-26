using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using TechStrap.AppHost;
using TechStrap.Tests.Shared;

namespace TechStrap.LocalStack.Tests;

/// <summary>The port block, the files that repeat parts of it, and the one-way reference rule for tools/.</summary>
public sealed class LocalStackPortsTests
{
    private const string AppHostProject = "tools/TechStrap.AppHost/TechStrap.AppHost.csproj";

    private static Dictionary<string, int> Ports() => typeof(LocalStackPorts)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(int))
        .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);

    [Fact]
    public void Ports_are_unique_and_inside_the_block()
    {
        var ports = Ports();

        ports.Count.ShouldBe(15);
        ports.Values.Distinct().Count().ShouldBe(15);
        ports.Values.ShouldAllBe(port => port >= 55200 && port <= 55299);
    }

    [Fact]
    public void Ports_have_the_agreed_values()
    {
        Ports().ShouldBe(
            new Dictionary<string, int>
            {
                ["Dashboard"] = 55200,
                ["DashboardOtlp"] = 55201,
                ["ResourceService"] = 55202,
                ["Postgres"] = 55210,
                ["MailpitSmtp"] = 55220,
                ["MailpitUi"] = 55221,
                ["ApiHttps"] = 55230,
                ["ApiHttp"] = 55231,
                ["AdminHttps"] = 55240,
                ["AdminHttp"] = 55241,
                ["PortalHttps"] = 55250,
                ["PortalHttp"] = 55251,
                ["Worker"] = 55260,
                ["WebhookCatcher"] = 55270,
                ["Authentik"] = 55290,
            },
            ignoreOrder: true);
    }

    [Fact]
    public void AppHost_launch_profile_matches_the_ports()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryRoot.Combine("tools", "TechStrap.AppHost", "Properties", "launchSettings.json")));
        var profile = document.RootElement.GetProperty("profiles").EnumerateObject().ShouldHaveSingleItem().Value;
        var environment = profile.GetProperty("environmentVariables");

        profile.GetProperty("applicationUrl").GetString().ShouldBe($"http://localhost:{LocalStackPorts.Dashboard}");
        environment.GetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL").GetString()
            .ShouldBe($"http://localhost:{LocalStackPorts.DashboardOtlp}");
        environment.GetProperty("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL").GetString()
            .ShouldBe($"http://localhost:{LocalStackPorts.ResourceService}");
        environment.GetProperty("ASPIRE_ALLOW_UNSECURED_TRANSPORT").GetString().ShouldBe("true");
        environment.GetProperty("DOTNET_ENVIRONMENT").GetString().ShouldBe("Development");
    }

    [Theory]
    [InlineData("src/TechStrap.Api", "https://localhost:55230;http://localhost:55231")]
    [InlineData("src/TechStrap.Admin", "https://localhost:55240;http://localhost:55241")]
    [InlineData("src/TechStrap.Portal", "https://localhost:55250;http://localhost:55251")]
    [InlineData("src/TechStrap.Worker", "http://localhost:55260")]
    [InlineData("tools/TechStrap.WebhookCatcher", "http://localhost:55270")]
    public void Application_launch_profiles_match_the_ports(string project, string urls)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryRoot.Combine([.. project.Split('/'), "Properties", "launchSettings.json"])));
        var entry = document.RootElement.GetProperty("profiles").EnumerateObject().ShouldHaveSingleItem();
        var profile = entry.Value;

        entry.Name.ShouldBe("aspire");
        profile.GetProperty("commandName").GetString().ShouldBe("Project");
        profile.GetProperty("launchBrowser").GetBoolean().ShouldBeFalse();
        profile.GetProperty("applicationUrl").GetString().ShouldBe(urls);
        profile.GetProperty("environmentVariables").GetProperty("ASPNETCORE_ENVIRONMENT").GetString()
            .ShouldBe("Development");
    }

    [Fact]
    public void Launch_profile_urls_come_from_the_port_block()
    {
        // Ties the literal URLs above to the constants the wiring uses.
        new[]
            {
                LocalStackPorts.ApiHttps, LocalStackPorts.ApiHttp, LocalStackPorts.AdminHttps, LocalStackPorts.AdminHttp,
                LocalStackPorts.PortalHttps, LocalStackPorts.PortalHttp, LocalStackPorts.Worker,
                LocalStackPorts.WebhookCatcher,
            }
            .ShouldBe([55230, 55231, 55240, 55241, 55250, 55251, 55260, 55270]);
    }

    [Fact]
    public void No_committed_config_names_the_capture_key()
    {
        // The key sends every webhook to a local catcher. Only the AppHost may set it, at run time.
        string[] skipped = ["bin", "obj", "node_modules", "local", ".git", ".superpowers", ".vs", ".claude", ".worktrees", "artifacts"];
        var root = RepositoryRoot.Find();
        var files = new List<string>();
        var folders = new Stack<string>([root]);
        while (folders.TryPop(out var folder))
        {
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                if (!skipped.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    folders.Push(child);
                }
            }

            files.AddRange(Directory.EnumerateFiles(folder).Where(file => IsCommittedConfig(root, file)));
        }

        var relative = files.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).ToList();
        relative.ShouldContain("docker-compose.yml");
        relative.ShouldContain("src/TechStrap.Worker/appsettings.json");
        relative.ShouldContain("src/TechStrap.Worker/.env.example");
        relative.ShouldContain(file => file.StartsWith("deploy/", StringComparison.Ordinal));

        files.Where(file => File.ReadAllText(file).Contains("TECHSTRAP_WEBHOOK_CAPTURE_URL", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    private static bool IsCommittedConfig(string root, string file)
    {
        var name = Path.GetFileName(file);
        return (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            || (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith("example", StringComparison.OrdinalIgnoreCase))
            || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith(Path.Combine(root, "deploy") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aspire_config_points_at_the_AppHost()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("aspire.config.json")));

        document.RootElement.GetProperty("appHost").GetProperty("path").GetString().ShouldBe(AppHostProject);
        File.Exists(RepositoryRoot.Combine(AppHostProject.Split('/'))).ShouldBeTrue();
    }

    [Fact]
    public void No_src_project_references_tools()
    {
        var tools = RepositoryRoot.Combine("tools") + Path.DirectorySeparatorChar;
        var projects = Directory.GetFiles(RepositoryRoot.Combine("src"), "*.csproj", SearchOption.AllDirectories);
        projects.ShouldNotBeEmpty();

        // Every Include counts: a ProjectReference, but also a linked source file or content item.
        var offenders =
            from project in projects
            from include in XDocument.Load(project).Descendants().Attributes("Include")
            let target = Path.GetFullPath(include.Value.Replace('\\', '/'), Path.GetDirectoryName(project)!)
            where target.StartsWith(tools, StringComparison.OrdinalIgnoreCase)
            select $"{Path.GetFileName(project)} -> {include.Value}";

        offenders.ShouldBeEmpty();
    }
}
