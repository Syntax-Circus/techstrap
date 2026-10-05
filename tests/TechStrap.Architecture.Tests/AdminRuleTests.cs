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
            ("src/TechStrap.Admin/Components/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/Queue.razor", "@attribute [ExcludeFromInteractiveRouting]"),
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
            ("src/TechStrap.Admin/Components/Pages/Error.razor", "<h1>Error</h1>"),
            ("src/TechStrap.Admin/Components/Pages/Error.razor.cs", "[ExcludeFromInteractiveRouting]\npublic partial class Error;"),
            ("src/TechStrap.Admin/Components/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldBe(["src/TechStrap.Admin/Components/Pages/Error is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data."]);
    }

    [Fact]
    public void An_allowlist_entry_with_no_matching_page_is_flagged()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/TechStrap.Admin/Components/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldContain(v => v.StartsWith("NotFound is in AdminRules.StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.StartsWith("StyleGuide is in AdminRules.StaticPages", StringComparison.Ordinal));
    }

    [Fact]
    public void A_comment_that_names_the_attribute_is_not_a_hit()
    {
        AdminRules.StaticPageViolations(
        [
            ("src/TechStrap.Admin/Components/Pages/Queue.razor", "// Pages marked [ExcludeFromInteractiveRouting] render statically."),
            ("src/TechStrap.Admin/Components/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/TechStrap.Admin/Components/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
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

    private const string Pages = "src/TechStrap.Admin/Components/Pages/";
    private const string Both = "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]";

    private static (string Path, string Text)[] ThreeStaticPagesPlus(params (string Path, string Text)[] extra) =>
    [
        (Pages + "Error.razor", Both),
        (Pages + "NotFound.razor", Both),
        (Pages + "StyleGuide.razor", Both),
        .. extra,
    ];

    [Fact]
    public void A_same_named_page_in_another_folder_is_flagged_and_the_real_page_is_reported_missing()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/TechStrap.Admin/Components/Other/Error.razor", Both),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]);

        violations.ShouldContain(v => v.Contains("Other/Error", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.StartsWith("Error is in AdminRules.StaticPages", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_separators_in_a_path_still_match_the_pinned_page_path()
    {
        AdminRules.StaticPageViolations(
        [
            (@"src\TechStrap.Admin\Components\Pages\Error.razor", Both),
            (@"src\TechStrap.Admin\Components\Pages\NotFound.razor", Both),
            (@"src\TechStrap.Admin\Components\Pages\StyleGuide.razor", Both),
        ]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("@attribute [ExcludeFromInteractiveRoutingAttribute]")]
    [InlineData("@attribute [Microsoft.AspNetCore.Components.ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [Microsoft.AspNetCore.Components.ExcludeFromInteractiveRoutingAttribute]")]
    [InlineData("@attribute [Foo(\"x\"), ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [Foo(\"a,b]\"), Bar, ExcludeFromInteractiveRoutingAttribute]")]
    [InlineData("@attribute [Foo, ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute  [ ExcludeFromInteractiveRouting ]")]
    public void Every_spelling_of_the_exclude_attribute_in_a_razor_directive_is_caught(string directive)
    {
        var violations = AdminRules.StaticPageViolations(ThreeStaticPagesPlus((Pages + "Queue.razor", "@attribute [AllowAnonymous]\n" + directive)));

        violations.ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[ExcludeFromInteractiveRoutingAttribute]\npublic partial class Queue;")]
    [InlineData("[Microsoft.AspNetCore.Components.ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[Microsoft.AspNetCore.Components.ExcludeFromInteractiveRoutingAttribute]\npublic partial class Queue;")]
    [InlineData("[Foo(\"x\"), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[A]\n[Foo(1, 2), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[A, ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    public void Every_spelling_of_the_exclude_attribute_in_a_code_behind_file_is_caught(string source)
    {
        AdminRules.StaticPageViolations(ThreeStaticPagesPlus((Pages + "Queue.razor.cs", source)))
            .ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
    }

    [Fact]
    public void The_exclude_attribute_in_a_plain_cs_file_is_caught()
    {
        AdminRules.StaticPageViolations(ThreeStaticPagesPlus((Pages + "Queue.cs", "[ExcludeFromInteractiveRouting]\npublic partial class Queue;")))
            .ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("@attribute [AllowAnonymousAttribute]\n@attribute [ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [Microsoft.AspNetCore.Authorization.AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute]\n@attribute [ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [Foo(\"x\"), AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]")]
    [InlineData("@attribute [AllowAnonymous, ExcludeFromInteractiveRouting]")]
    public void Every_accepted_spelling_of_AllowAnonymous_satisfies_the_rule(string directives)
    {
        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", directives),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void AllowAnonymous_on_a_nested_type_in_a_code_block_does_not_satisfy_the_rule()
    {
        var razor = "@attribute [ExcludeFromInteractiveRouting]\n@code {\n    [AllowAnonymous]\n    private sealed class Nested { }\n}";

        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", razor),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).ShouldBe([Pages + "Error is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data."]);
    }

    [Fact]
    public void AllowAnonymous_on_the_page_class_in_the_code_behind_satisfies_the_rule_but_on_a_nested_type_does_not()
    {
        var onPage = "[AllowAnonymous]\npublic partial class Error\n{\n}";
        var onNested = "public partial class Error\n{\n    [AllowAnonymous]\n    private sealed class Nested { }\n}";

        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", "@attribute [ExcludeFromInteractiveRouting]"),
            (Pages + "Error.razor.cs", onPage),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).ShouldBeEmpty();

        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", "@attribute [ExcludeFromInteractiveRouting]"),
            (Pages + "Error.razor.cs", onNested),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).Count.ShouldBe(1);
    }

    [Fact]
    public void ComponentSources_does_not_drop_a_checkout_that_lives_under_a_bin_obj_or_node_modules_folder()
    {
        var top = Path.Combine(Path.GetTempPath(), "techstrap-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(top, "bin", "obj", "node_modules", "repo");
        var admin = Path.Combine(root, "src", ReferenceRules.Admin);
        try
        {
            Directory.CreateDirectory(Path.Combine(admin, "Components"));
            Directory.CreateDirectory(Path.Combine(admin, "obj"));
            Directory.CreateDirectory(Path.Combine(admin, "bin", "Debug"));
            File.WriteAllText(Path.Combine(admin, "Components", "Kept.razor"), "x");
            File.WriteAllText(Path.Combine(admin, "obj", "Generated.razor.cs"), "x");
            File.WriteAllText(Path.Combine(admin, "bin", "Debug", "Copied.razor"), "x");

            AdminRules.ComponentSources(root).Select(f => Path.GetFileName(f.Path)).ShouldBe(["Kept.razor"]);
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }

    [Theory]
    [InlineData("<script data-src=\"x.js\">alert(1)</script>")]
    [InlineData("<script nosrc=\"x\">alert(1)</script>")]
    [InlineData("<script>\n  var a = 1;\n</script>")]
    public void A_script_whose_attribute_only_ends_in_src_is_still_inline(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", markup)]).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("<script src=\"a.js\"></script>")]
    [InlineData("<script type=\"module\" src=\"a.js\"></script>")]
    [InlineData("<!-- <script>x()</script> -->")]
    [InlineData("<!--\n<style>a{}</style>\n-->")]
    [InlineData("@* <script>x()</script> *@")]
    [InlineData("@*\n<style>a{}</style>\n*@")]
    [InlineData("<Style Media=\"print\" />")]
    [InlineData("<Script />")]
    [InlineData("<script-x>a</script-x>")]
    [InlineData("<style-sheet />")]
    [InlineData("<Scripts />")]
    public void Comments_components_and_look_alike_tags_are_not_inline_markup(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Ok.razor", markup)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_style_element_after_a_closed_comment_is_still_flagged()
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", "<!-- note -->\n@* note *@\n<style>a{}</style>")]).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("[A] [ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[A,\n    ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[A(1)] [B] [Foo(new[]{1}), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[Foo(']'), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[Foo(@\"a\\\"), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[Foo(@\"a\"\"]\"), ExcludeFromInteractiveRouting]\npublic partial class Queue;")]
    [InlineData("[AllowAnonymous] [ExcludeFromInteractiveRouting] public partial class Queue;")]
    public void Same_line_multi_line_and_tricky_literal_attribute_lists_are_caught(string source)
    {
        AdminRules.StaticPageViolations(ThreeStaticPagesPlus((Pages + "Queue.razor.cs", source)))
            .ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[AllowAnonymous] [ExcludeFromInteractiveRouting] public partial class Error;")]
    [InlineData("[Foo(']')] [AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[Foo(@\"a\\\"), AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[Foo(@\"a\"\"]\"), AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[Foo(new[]{1}), AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[A,\n    AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("// [assembly: nothing]\n/* ] */ [AllowAnonymous, ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    public void Tricky_but_valid_code_behind_files_satisfy_the_rule(string source)
    {
        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", "<h1>Error</h1>"),
            (Pages + "Error.razor.cs", source),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[assembly: AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[return: AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    [InlineData("[method: AllowAnonymous]\n[ExcludeFromInteractiveRouting]\npublic partial class Error;")]
    public void AllowAnonymous_with_an_assembly_or_return_target_does_not_count_as_on_the_page(string source)
    {
        AdminRules.StaticPageViolations(
        [
            (Pages + "Error.razor", "<h1>Error</h1>"),
            (Pages + "Error.razor.cs", source),
            (Pages + "NotFound.razor", Both),
            (Pages + "StyleGuide.razor", Both),
        ]).ShouldBe([Pages + "Error is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data."]);
    }

    [Theory]
    [InlineData("Queue.razor.cs", "[ExcludeFromInteractiveRouting\npublic partial class Queue;")]
    [InlineData("Queue.razor.cs", "[A] [AllowAnonymous\npublic partial class Queue;")]
    [InlineData("Queue.cs", "public partial class Queue { string s = \"abc; }")]
    [InlineData("Queue.cs", "public partial class Queue { string s = @\"abc; }")]
    [InlineData("Queue.cs", "public partial class Queue { char c = 'ab'; }")]
    [InlineData("Queue.cs", "/* never closed\npublic partial class Queue;")]
    [InlineData("Queue.razor", "@attribute [AllowAnonymous")]
    [InlineData("Queue.razor", "@attribute [Foo(\"x]\n<h1>x</h1>")]
    [InlineData("Queue.razor", "<h1>x</h1>\n<!-- never closed")]
    [InlineData("Queue.razor", "<h1>x</h1>\n@* never closed")]
    public void A_file_that_cannot_be_parsed_is_a_violation_not_a_skip(string file, string text)
    {
        AdminRules.StaticPageViolations(ThreeStaticPagesPlus((Pages + file, text)))
            .ShouldContain(v => v.StartsWith("could not parse " + Pages + file, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<!-- never closed\n<script>x()</script>")]
    [InlineData("@* never closed\n<script>x()</script>")]
    [InlineData("<script nonce=\"src=1\">x()</script>")]
    [InlineData("<script nonce='src=1' defer>x()</script>")]
    [InlineData("<p>a /* b</p>\n<script>x()</script>\n<p>c */</p>")]
    [InlineData("<p>a /* b</p>\n<style>a{}</style>\n<p>c */</p>")]
    public void Unterminated_comments_attribute_values_and_a_stray_slash_star_do_not_hide_inline_markup(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", markup)]).Count.ShouldBe(1);
    }

    [Fact]
    public void An_unterminated_comment_is_reported_as_a_parse_failure()
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", "<!-- never closed\n<h1>x</h1>")])
            .ShouldBe(["could not parse Components/Bad.razor: unterminated comment"]);
    }

    [Theory]
    [InlineData("<script src=\"@Assets[\"_framework/blazor.web.js\"]\"></script>")]
    [InlineData("<script defer\n  src=\"a.js\"\n  nonce=\"n\"></script>")]
    [InlineData("<script nonce=\"n\" src='a.js'></script>")]
    [InlineData("<script SRC=\"a.js\"/>")]
    [InlineData("<p>a /* b</p>\n<script src=\"a.js\"></script>")]
    public void A_script_with_its_own_src_attribute_is_allowed(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Ok.razor", markup)]).ShouldBeEmpty();
    }
}
