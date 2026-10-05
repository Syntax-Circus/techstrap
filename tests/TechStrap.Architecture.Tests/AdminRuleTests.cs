namespace TechStrap.Architecture.Tests;

public sealed class AdminRuleTests
{
    private static ProjectNode AdminWith(params string[] packages) =>
        new(ReferenceRules.Admin, new HashSet<string>(), packages.ToHashSet(), new HashSet<string>());

    [Fact]
    public void The_Admin_project_references_only_allowed_packages()
    {
        var admin = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Admin];

        admin.PackageReferences.ShouldContain("SyntaxCircus.Blazor.Auth", "the scan must read the real Admin project");
        AdminRules.PackageViolations(admin).ShouldBeEmpty();
    }

    [Fact]
    public void The_rule_flags_EF_Npgsql_and_any_unlisted_package()
    {
        var violations = AdminRules.PackageViolations(AdminWith("SyntaxCircus.Common", "Microsoft.EntityFrameworkCore", "Npgsql", "Newtonsoft.Json"));

        violations.Count.ShouldBe(3);
        violations.ShouldContain(v => v.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Npgsql", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Newtonsoft.Json", StringComparison.Ordinal));
    }

    [Fact]
    public void No_component_uses_HttpClient()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal), "the scan must see the Admin components");
        files.ShouldContain(f => f.Path.EndsWith(".razor.cs", StringComparison.Ordinal), "the scan must see the code-behind files");
        AdminRules.HttpClientViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Page.razor", "@inject HttpClient Http")]
    [InlineData("Page.razor", "@inject IHttpClientFactory Factory")]
    [InlineData("Page.razor.cs", "[Inject] public HttpClient Http { get; set; } = default!;")]
    [InlineData("Page.razor.cs", "public Page(HttpClient http) { }")]
    public void A_component_that_uses_HttpClient_is_flagged(string path, string text)
    {
        AdminRules.HttpClientViolations([(path, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void HttpClient_outside_a_component_is_not_flagged_and_a_look_alike_name_is_not_a_hit()
    {
        AdminRules.HttpClientViolations([("Clients/ApiConnection.cs", "HttpClient http"), ("Page.razor", "@inject IBlazorCircuitHttpClientFactory Factory")])
            .ShouldBeEmpty("Clients/ may use HttpClient; a different type whose name ends the same is not the rule's target");
    }

    [Fact]
    public void Only_the_allowed_pages_are_excluded_from_interactive_routing_and_each_is_anonymous()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        AdminRules.StaticPageViolations(files).ShouldBeEmpty();
        AdminRules.StaticPages.Order().ShouldBe(["Error", "NotFound", "StyleGuide"]);
    }

    [Fact]
    public void A_data_page_excluded_from_interactive_routing_is_flagged_twice_when_it_is_not_anonymous()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/Queue.razor", "@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.Count.ShouldBe(2);
        violations.ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("AllowAnonymous", StringComparison.Ordinal));
    }

    [Fact]
    public void An_allowed_page_that_is_not_anonymous_is_flagged_and_the_attribute_is_found_in_a_code_behind_file()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "<h1>Error</h1>"),
            ("src/Pages/Error.razor.cs", "[ExcludeFromInteractiveRouting]\npublic partial class Error;"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldBe(["src/Pages/Error is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data."]);
    }

    [Fact]
    public void An_allowlist_entry_with_no_matching_page_is_flagged()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldContain(v => v.StartsWith("NotFound is in AdminRules.StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.StartsWith("StyleGuide is in AdminRules.StaticPages", StringComparison.Ordinal));
    }

    [Fact]
    public void A_comment_that_names_the_attribute_is_not_a_hit()
    {
        AdminRules.StaticPageViolations(
        [
            ("src/Pages/Queue.razor", "// Pages marked [ExcludeFromInteractiveRouting] render statically."),
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void No_razor_file_has_an_inline_script_or_a_style_element()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal) && f.Text.Contains("blazor.web.js", StringComparison.Ordinal), "App.razor loads the framework script by src");
        AdminRules.InlineMarkupViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT type=\"text/javascript\">x()</SCRIPT>")]
    [InlineData("<style>.a { color: red }</style>")]
    [InlineData("<style media=\"print\"></style>")]
    public void An_inline_script_or_style_element_is_flagged(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", markup)]).Count.ShouldBe(1);
    }

    [Fact]
    public void An_external_script_and_an_import_map_component_are_not_flagged()
    {
        AdminRules.InlineMarkupViolations([("Components/App.razor", "<ImportMap />\n<script src=\"@Assets[\"_framework/blazor.web.js\"]\"></script>")]).ShouldBeEmpty();
    }
}
