namespace TechStrap.Architecture.Tests;

public sealed class ClientMauiRuleTests
{
    private const string CleanProjectFile = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>TechStrap.Client.Maui</PackageId></PropertyGroup></Project>";

    private static ProjectNode MauiWith(string[]? packages = null, string[]? projects = null, string[]? frameworks = null) =>
        new(ReferenceRules.ClientMaui, (projects ?? [ReferenceRules.Client, ReferenceRules.Contracts]).ToHashSet(), (packages ?? []).ToHashSet(), (frameworks ?? []).ToHashSet());

    [Fact]
    public void The_Client_Maui_project_references_only_allowed_packages_and_projects_and_never_sets_UseMaui()
    {
        var root = ProjectGraph.FindRepositoryRoot();
        var project = ProjectGraph.LoadSourceProjects(root)[ReferenceRules.ClientMaui];
        var text = File.ReadAllText(Path.Combine(root, "src", ReferenceRules.ClientMaui, ReferenceRules.ClientMaui + ".csproj"));

        project.PackageReferences.ShouldContain("Microsoft.Maui.Essentials", "the scan must read the real Client.Maui project");
        project.ProjectReferences.Order(StringComparer.Ordinal).ShouldBe([ReferenceRules.Client, ReferenceRules.Contracts]);
        project.FrameworkReferences.ShouldBeEmpty();
        ClientMauiRules.Violations(project, text).ShouldBeEmpty();
    }

    [Fact]
    public void The_Client_Maui_allow_list_is_exactly_the_three_reviewed_packages()
    {
        ClientMauiRules.AllowedPackages.Order(StringComparer.Ordinal).ShouldBe(
        [
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Maui.Essentials",
        ]);
    }

    [Fact]
    public void A_clean_fixture_passes()
    {
        ClientMauiRules.Violations(MauiWith(packages: ["Microsoft.Maui.Essentials"]), CleanProjectFile).ShouldBeEmpty();
    }

    [Fact]
    public void An_extra_package_is_flagged()
    {
        var violations = ClientMauiRules.Violations(MauiWith(packages: ["Microsoft.Maui.Essentials", "Microsoft.Maui.Controls"]), CleanProjectFile);

        violations.ShouldHaveSingleItem().ShouldContain("TechStrap.Client.Maui must not reference package Microsoft.Maui.Controls");
    }

    [Theory]
    [InlineData(ReferenceRules.Application)]
    [InlineData(ReferenceRules.Infrastructure)]
    [InlineData(ReferenceRules.Domain)]
    [InlineData(ReferenceRules.Hosting)]
    public void An_extra_project_reference_is_flagged(string project)
    {
        var violations = ClientMauiRules.Violations(MauiWith(projects: [ReferenceRules.Client, ReferenceRules.Contracts, project]), CleanProjectFile);

        violations.ShouldHaveSingleItem().ShouldContain($"TechStrap.Client.Maui must reference only {ReferenceRules.Client} and {ReferenceRules.Contracts}, not {project}");
    }

    [Fact]
    public void A_framework_reference_is_flagged()
    {
        var violations = ClientMauiRules.Violations(MauiWith(frameworks: ["Microsoft.AspNetCore.App"]), CleanProjectFile);

        violations.ShouldHaveSingleItem().ShouldContain("TechStrap.Client.Maui must not reference a framework (Microsoft.AspNetCore.App)");
    }

    [Fact]
    public void UseMaui_in_the_project_file_is_flagged()
    {
        var text = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>";

        var violations = ClientMauiRules.Violations(MauiWith(), text);

        violations.ShouldHaveSingleItem().ShouldContain("TechStrap.Client.Maui must not set UseMaui");
    }
}