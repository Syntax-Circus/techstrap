namespace TechStrap.Architecture.Tests;

public sealed class ProjectReferenceDirectionTests
{
    private static ProjectNode Node(string name, string[]? projects = null, string[]? packages = null, string[]? frameworks = null) =>
        new(
            name,
            (projects ?? []).ToHashSet(),
            (packages ?? []).ToHashSet(),
            (frameworks ?? []).ToHashSet());

    [Fact]
    public void Source_projects_follow_the_allowed_reference_direction()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        ReferenceRules.Evaluate(graph).ShouldBeEmpty();
    }

    [Fact]
    public void Solution_contains_the_eleven_source_projects()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        graph.Keys.Order().ShouldBe(ReferenceRules.AllowedProjectReferences.Keys.Order());
    }

    [Fact]
    public void Domain_and_Contracts_reference_no_project_package_or_framework()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        foreach (var name in new[] { ReferenceRules.Domain, ReferenceRules.Contracts })
        {
            graph[name].ProjectReferences.ShouldBeEmpty(name);
            graph[name].PackageReferences.ShouldBeEmpty(name);
            graph[name].FrameworkReferences.ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void Admin_and_Portal_reference_only_Contracts_and_Hosting()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        // Admin signs agents in and logs, so it uses the shared Hosting helpers; Portal joins in PHASE-09. Neither may ever see Application, Infrastructure or Domain.
        graph[ReferenceRules.Admin].ProjectReferences.Order().ShouldBe([ReferenceRules.Contracts, ReferenceRules.Hosting]);
        graph[ReferenceRules.Portal].ProjectReferences.ShouldContain(ReferenceRules.Contracts);
        graph[ReferenceRules.Portal].ProjectReferences.Except([ReferenceRules.Contracts, ReferenceRules.Hosting]).ShouldBeEmpty();
    }

    [Fact]
    public void Hosting_references_no_TechStrap_project_and_only_the_logging_and_telemetry_packages()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        graph[ReferenceRules.Hosting].ProjectReferences.ShouldBeEmpty();
        graph[ReferenceRules.Hosting].PackageReferences.Order().ShouldBe(["SyntaxCircus.AspNetCore.Serilog", "SyntaxCircus.Observability"]);
    }

    [Fact]
    public void Infrastructure_no_longer_references_a_logging_package()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        graph[ReferenceRules.Infrastructure].PackageReferences.ShouldNotContain("SyntaxCircus.AspNetCore.Serilog");
    }

    [Fact]
    public void Rules_flag_Hosting_referencing_a_project_and_Infrastructure_referencing_Hosting()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Hosting] = Node(ReferenceRules.Hosting, projects: [ReferenceRules.Contracts]),
            [ReferenceRules.Infrastructure] = Node(ReferenceRules.Infrastructure, projects: [ReferenceRules.Hosting]),
        };

        var violations = ReferenceRules.Evaluate(graph);

        violations.ShouldContain($"{ReferenceRules.Hosting} must not reference {ReferenceRules.Contracts}.");
        violations.ShouldContain($"{ReferenceRules.Infrastructure} must not reference {ReferenceRules.Hosting}.");
    }

    // The tests below feed the rules deliberately bad graphs: they prove each rule can fail.

    [Fact]
    public void Rules_flag_Admin_referencing_Application()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Admin] = Node(ReferenceRules.Admin, projects: [ReferenceRules.Contracts, ReferenceRules.Application]),
        };

        ReferenceRules.Evaluate(graph).ShouldContain($"{ReferenceRules.Admin} must not reference {ReferenceRules.Application}.");
    }

    [Fact]
    public void Rules_flag_Application_referencing_Infrastructure_or_a_host()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Application] = Node(
                ReferenceRules.Application,
                projects: [ReferenceRules.Domain, ReferenceRules.Infrastructure, ReferenceRules.Api]),
        };

        var violations = ReferenceRules.Evaluate(graph);

        violations.ShouldContain($"{ReferenceRules.Application} must not reference {ReferenceRules.Infrastructure}.");
        violations.ShouldContain($"{ReferenceRules.Application} must not reference {ReferenceRules.Api}.");
    }

    [Fact]
    public void Rules_flag_Infrastructure_referencing_a_host()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Infrastructure] = Node(ReferenceRules.Infrastructure, projects: [ReferenceRules.Worker]),
        };

        ReferenceRules.Evaluate(graph).ShouldContain($"{ReferenceRules.Infrastructure} must not reference {ReferenceRules.Worker}.");
    }

    [Fact]
    public void Rules_flag_Domain_and_Contracts_with_any_reference()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Domain] = Node(ReferenceRules.Domain, projects: [ReferenceRules.Contracts]),
            [ReferenceRules.Contracts] = Node(
                ReferenceRules.Contracts,
                packages: ["Microsoft.EntityFrameworkCore"],
                frameworks: ["Microsoft.AspNetCore.App"]),
        };

        var violations = ReferenceRules.Evaluate(graph);

        violations.ShouldContain($"{ReferenceRules.Domain} must not reference {ReferenceRules.Contracts}.");
        violations.ShouldContain($"{ReferenceRules.Contracts} must not reference package Microsoft.EntityFrameworkCore.");
        violations.ShouldContain($"{ReferenceRules.Contracts} must not reference framework Microsoft.AspNetCore.App.");
    }

    [Fact]
    public void Rules_flag_extra_packages_in_Application()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Application] = Node(
                ReferenceRules.Application,
                projects: [ReferenceRules.Domain, ReferenceRules.Contracts],
                packages: ["SyntaxCircus.Common", "Npgsql"]),
        };

        ReferenceRules.Evaluate(graph)
            .ShouldContain($"{ReferenceRules.Application} may reference only SyntaxCircus.Common, not package Npgsql.");
    }

    [Fact]
    public void Rules_flag_an_unknown_project()
    {
        var graph = new Dictionary<string, ProjectNode> { ["TechStrap.Mystery"] = Node("TechStrap.Mystery") };

        ReferenceRules.Evaluate(graph)
            .ShouldContain("TechStrap.Mystery: unknown project. Add it to ReferenceRules.AllowedProjectReferences.");
    }
}
