namespace TechStrap.Architecture.Tests;

public sealed class ClientRuleTests
{
    private static ProjectNode ClientWith(string[]? packages = null, string[]? projects = null, string[]? frameworks = null) =>
        new(ReferenceRules.Client, (projects ?? []).ToHashSet(), (packages ?? []).ToHashSet(), (frameworks ?? []).ToHashSet());

    [Fact]
    public void The_Client_project_references_only_allowed_packages_and_the_Contracts_project()
    {
        var client = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Client];

        client.PackageReferences.ShouldContain("SyntaxCircus.Http.Resilience", "the scan must read the real Client project");
        client.ProjectReferences.ShouldBe([ReferenceRules.Contracts]);
        client.FrameworkReferences.ShouldBeEmpty();
        ClientRules.Violations(client).ShouldBeEmpty();
    }

    [Fact]
    public void The_Client_allow_list_is_exactly_the_five_reviewed_packages()
    {
        ClientRules.AllowedPackages.Order(StringComparer.Ordinal).ShouldBe(
        [
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Http",
            "Microsoft.Extensions.Options",
            "SyntaxCircus.Common",
            "SyntaxCircus.Http.Resilience",
        ]);
    }

    [Fact]
    public void An_extra_package_is_flagged()
    {
        var violations = ClientRules.Violations(ClientWith(packages: ["SyntaxCircus.Common", "Newtonsoft.Json"], projects: [ReferenceRules.Contracts]));

        var violation = violations.ShouldHaveSingleItem();
        violation.ShouldContain("TechStrap.Client must not reference package Newtonsoft.Json");
    }

    [Theory]
    [InlineData(ReferenceRules.Application)]
    [InlineData(ReferenceRules.Infrastructure)]
    [InlineData(ReferenceRules.Domain)]
    [InlineData(ReferenceRules.Hosting)]
    public void An_extra_project_reference_is_flagged(string project)
    {
        var violations = ClientRules.Violations(ClientWith(projects: [ReferenceRules.Contracts, project]));

        violations.ShouldHaveSingleItem().ShouldContain($"TechStrap.Client must not reference project {project}");
    }

    [Fact]
    public void A_framework_reference_is_flagged()
    {
        var violations = ClientRules.Violations(ClientWith(projects: [ReferenceRules.Contracts], frameworks: ["Microsoft.AspNetCore.App"]));

        violations.ShouldHaveSingleItem().ShouldContain("TechStrap.Client must not reference framework Microsoft.AspNetCore.App");
    }
}
