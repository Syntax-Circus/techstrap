using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>
/// P09-T01 validation: "route constants used by every page (no inline route strings repeated)". Every page declares its route with
/// <c>@attribute [Route(PortalRoutes.XTemplate)]</c> and every link is built by a <c>PortalRoutes</c> builder, so a route changes in one place.
/// </summary>
public sealed partial class RouteLiteralTests
{
    private static IEnumerable<(string Path, string Text)> PortalSources() =>
        Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => (Path.GetRelativePath(RepositoryRoot.Combine("src", "TechStrap.Portal"), path).Replace('\\', '/'), File.ReadAllText(path)));

    // A quoted or attribute-quoted string that starts a Portal route: "/p/", "/t/", "/p" and "/t" alone, or the root-relative pages.
    [GeneratedRegex("""["'](/p|/t)(/[^"']*)?["']|href="/(p|t)/""", RegexOptions.CultureInvariant)]
    private static partial Regex RouteLiteral();

    [Fact]
    public void The_scan_sees_the_portal_sources()
    {
        PortalSources().Count().ShouldBeGreaterThan(10);
        PortalSources().ShouldContain(source => source.Path == "Routing/PortalRoutes.cs");
    }

    [Fact]
    public void No_page_declares_its_route_with_a_string_literal()
    {
        var offenders = PortalSources().Where(source => source.Path.EndsWith(".razor", StringComparison.Ordinal) && Regex.IsMatch(source.Text, @"(?m)^@page\s")).Select(source => source.Path);

        offenders.ShouldBeEmpty("use @attribute [Route(PortalRoutes.XTemplate)]");
    }

    [Fact]
    public void No_source_outside_PortalRoutes_spells_a_p_or_t_route()
    {
        var offenders = PortalSources()
            .Where(source => source.Path != "Routing/PortalRoutes.cs")
            .Where(source => RouteLiteral().IsMatch(source.Text))
            .Select(source => source.Path);

        offenders.ShouldBeEmpty("build the address with a PortalRoutes builder");
    }
}
