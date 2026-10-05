using System.Reflection;
using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;

namespace TechStrap.Api.Tests.Kb;

/// <summary>
/// The Admin branches on the knowledge base error codes the API sends (<see cref="ApiErrorCodes"/>). The API writes them as string literals in its Domain, Application and Infrastructure code, and the Admin may
/// reference only Contracts and Hosting, so neither side can use the other's constants. This is the one place both are visible: the codes the Admin names and the <c>kb-</c> codes the API source sends
/// must be the same set, so a renamed or added API code fails here instead of turning into a generic failure on a screen.
/// </summary>
public sealed partial class KbWireCodeParityTests
{
    [GeneratedRegex("\"(kb-[a-z]+(?:-[a-z]+)*)\"")]
    private static partial Regex CodeLiteral();

    private static SortedSet<string> AdminCodes() =>
    [
        .. typeof(ApiErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.StartsWith("Kb", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    private static SortedSet<string> ApiCodes()
    {
        var root = FindRepositoryRoot();
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var project in new[] { "TechStrap.Domain", "TechStrap.Application", "TechStrap.Infrastructure", "TechStrap.Api", "TechStrap.Contracts" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                foreach (Match match in CodeLiteral().Matches(File.ReadAllText(file)))
                {
                    codes.Add(match.Groups[1].Value);
                }
            }
        }

        return codes;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_codes_the_admin_names_are_the_codes_the_api_sends()
    {
        var admin = AdminCodes();
        var api = ApiCodes();

        admin.ShouldNotBeEmpty();
        api.Except(admin).ShouldBeEmpty("an API knowledge base code the Admin has no constant for");
        admin.Except(api).ShouldBeEmpty("an Admin knowledge base code the API never sends");
    }
}
