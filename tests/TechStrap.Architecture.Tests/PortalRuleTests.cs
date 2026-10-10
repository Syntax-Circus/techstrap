namespace TechStrap.Architecture.Tests;

public sealed class PortalRuleTests
{
    private const string Page = "src/TechStrap.Portal/Components/Pages/Page.razor";
    private const string Code = "src/TechStrap.Portal/Products/Thing.cs";

    private static ProjectNode PortalWith(params string[] packages) =>
        new(ReferenceRules.Portal, new HashSet<string>(), packages.ToHashSet(), new HashSet<string>());

    // ---- Packages -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Portal_project_references_only_allowed_packages()
    {
        var portal = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Portal];

        portal.PackageReferences.ShouldContain("SyntaxCircus.Blazor.Seo", "the scan must read the real Portal project");
        PortalRules.PackageViolations(portal).ShouldBeEmpty();
    }

    [Fact]
    public void The_Portal_allow_list_is_exactly_what_the_project_references_today()
    {
        PortalRules.AllowedPackages.Order(StringComparer.Ordinal).ShouldBe(
        [
            "AspNetCore.SassCompiler",
            "GitVersion.MsBuild",
            "Microsoft.Web.LibraryManager.Build",
            "SyntaxCircus.AspNetCore.Common",
            "SyntaxCircus.AspNetCore.Serilog",
            "SyntaxCircus.Blazor.Components",
            "SyntaxCircus.Blazor.Seo",
            "SyntaxCircus.Common",
            "SyntaxCircus.DotEnv",
            "SyntaxCircus.Http.Resilience",
            "SyntaxCircus.Observability",
        ]);
    }

    [Fact]
    public void The_rule_flags_EF_Npgsql_Auth_and_any_unlisted_package()
    {
        var violations = PortalRules.PackageViolations(PortalWith("SyntaxCircus.Common", "Microsoft.EntityFrameworkCore", "Npgsql", "SyntaxCircus.Blazor.Auth", "Newtonsoft.Json"));

        violations.Count.ShouldBe(4);
        violations.ShouldContain(v => v.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Npgsql", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("SyntaxCircus.Blazor.Auth", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Newtonsoft.Json", StringComparison.Ordinal));
        violations.ShouldAllBe(v => v.Contains("TechStrap.Portal", StringComparison.Ordinal));
    }

    // ---- HttpClient only in Clients/ ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_outside_Clients_uses_HttpClient()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal), "the scan must see the Portal components");
        files.ShouldContain(f => f.Path.EndsWith(".razor.cs", StringComparison.Ordinal), "the scan must see the code-behind files");
        files.ShouldContain(f => f.Path.EndsWith("Clients/ApiConnection.cs", StringComparison.Ordinal) && f.Text.Contains("IHttpClientFactory", StringComparison.Ordinal), "the one place that may use it must be in the scan");
        PortalRules.HttpClientViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject HttpClient Http")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject IHttpClientFactory Factory")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "[Inject] public HttpClient Http { get; set; } = default!;")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "public Page(HttpClient http) { }")]
    [InlineData("src/TechStrap.Portal/Products/Base.cs", "protected HttpClient Http => null!;")]
    [InlineData("src/TechStrap.Portal/Program.cs", "builder.Services.AddHttpClient(\"x\");")]
    public void A_file_outside_Clients_that_uses_HttpClient_is_flagged(string path, string text)
    {
        PortalRules.HttpClientViolations([(path, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_comment_that_names_HttpClient_is_not_a_hit()
    {
        PortalRules.HttpClientViolations(
        [
            ("src/TechStrap.Portal/Program.cs", "// the host-wide HttpClient logging default\nvar x = 1; // IHttpClientFactory is only in Clients/"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor", "@* never inject HttpClient here *@<p>x</p>"),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void HttpClient_inside_Clients_is_not_flagged_and_a_look_alike_name_is_not_a_hit()
    {
        PortalRules.HttpClientViolations(
        [
            ("src/TechStrap.Portal/Clients/ApiConnection.cs", "HttpClient http; IHttpClientFactory factory;"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject IPublicProductClient Products"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "private HttpContext Context; private IHttpContextAccessor Accessor;"),
        ]).ShouldBeEmpty("Clients/ may use HttpClient; a different type whose name resembles it is not the rule's target");
    }

    // ---- No inline script or style ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_has_an_inline_script_a_style_element_the_import_map_or_an_event_handler_attribute()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal) && f.Text.Contains("blazor.web.js", StringComparison.Ordinal), "App.razor loads the framework script by src");
        PortalRules.InlineMarkupViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<style>.a { color: red }</style>")]
    [InlineData("<ImportMap />")]
    [InlineData("<button onclick=\"x()\">Go</button>")]
    public void A_deliberate_inline_script_style_import_map_or_handler_is_flagged(string markup)
    {
        PortalRules.InlineMarkupViolations([(Page, markup)]).Count.ShouldBe(1);
    }

    [Fact]
    public void An_external_script_is_not_flagged()
    {
        PortalRules.InlineMarkupViolations([(Page, "<script src=\"app.js\"></script>")]).ShouldBeEmpty();
    }

    // ---- MarkupString sites -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void In_09c_exactly_KbArticleBody_and_CustomerMessageBody_turn_text_into_markup()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.Count.ShouldBeGreaterThan(20, "the scan must see the Portal sources");
        PortalRules.MarkupStringSites.ShouldBe(
            ["Components/Kb/KbArticleBody.razor", "Components/Tickets/CustomerMessageBody.razor"],
            "09b added CustomerMessageBody (the API sanitizes the message body) and 09c added KbArticleBody (the API sanitizes the article with the same rules, D-044); a third site is a design decision, argued in its own commit");
        PortalRules.MarkupStringViolations(files).ShouldBeEmpty();
        files.Count(file => file.Text.Contains("MarkupString", StringComparison.Ordinal)).ShouldBe(2, "the word appears in those two files and nowhere else, comments included");
    }

    [Theory]
    [InlineData(Page, "@((MarkupString)Html)")]
    [InlineData(Page, "@(new MarkupString(Html))")]
    [InlineData(Page, "<div>@((Microsoft.AspNetCore.Components.MarkupString)Html)</div>")]
    [InlineData(Code, "var m = new MarkupString(html);")]
    [InlineData(Code, "builder.AddMarkupContent(0, html);")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "public MarkupString Body => (MarkupString)Html;")]
    public void A_deliberate_markup_site_is_flagged_when_it_is_not_on_the_allow_list(string path, string text)
    {
        var violations = PortalRules.MarkupStringViolations([(path, text)], []);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain(path.Replace("src/TechStrap.Portal/", string.Empty, StringComparison.Ordinal));
    }

    private const string ArticleBody = "src/TechStrap.Portal/Components/Kb/KbArticleBody.razor";
    private const string MessageBody = "src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor";
    private const string Markup = "<div>@((MarkupString)Html)</div>";
    private const string Encoded = "<div>@Html</div>";

    [Fact]
    public void The_real_allow_list_passes_for_its_two_files_and_flags_the_same_text_anywhere_else()
    {
        PortalRules.MarkupStringViolations([(ArticleBody, Markup), (MessageBody, Markup)]).ShouldBeEmpty();
        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Tickets/MessageThread.razor", Markup), (ArticleBody, Markup), (MessageBody, Markup)])
            .ShouldHaveSingleItem().ShouldContain("MessageThread.razor");
        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Pages/KbArticle.razor", "<div>@((MarkupString)article.Html)</div>"), (ArticleBody, Markup), (MessageBody, Markup)])
            .ShouldHaveSingleItem().ShouldContain("KbArticle.razor");
        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Kb/KbArticleCard.razor", "<p>@((MarkupString)Summary)</p>"), (ArticleBody, Markup), (MessageBody, Markup)])
            .ShouldHaveSingleItem().ShouldContain("KbArticleCard.razor");
    }

    [Fact]
    public void A_listed_file_that_no_longer_uses_it_and_a_missing_one_are_flagged()
    {
        PortalRules.MarkupStringViolations([(ArticleBody, Encoded), (MessageBody, Markup)]).ShouldHaveSingleItem().ShouldContain("KbArticleBody.razor");
        PortalRules.MarkupStringViolations([(ArticleBody, Markup), (MessageBody, Encoded)]).ShouldHaveSingleItem().ShouldContain("CustomerMessageBody.razor");
        PortalRules.MarkupStringViolations([(ArticleBody, Markup)]).ShouldHaveSingleItem().ShouldContain("no longer");
    }

    [Fact]
    public void A_listed_site_passes_and_an_unlisted_one_and_a_listed_file_that_no_longer_uses_it_are_flagged()
    {
        string[] allowed = ["Features/Kb/KbArticleBody.razor"];

        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "@((MarkupString)Html)")], allowed).ShouldBeEmpty();
        PortalRules.MarkupStringViolations(
        [
            ("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "@((MarkupString)Html)"),
            ("src/TechStrap.Portal/Features/Kb/KbArticleCard.razor", "@((MarkupString)Summary)"),
        ], allowed).Count.ShouldBe(1);
        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "<div>@Html</div>")], allowed)
            .ShouldHaveSingleItem().ShouldContain("no longer");
    }

    // ---- Static server rendering only ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_opts_in_to_an_interactive_render_mode()
    {
        PortalRules.InteractivityViolations(PortalRules.Sources(ProjectGraph.FindRepositoryRoot())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Program.cs", "builder.Services.AddRazorComponents().AddInteractiveServerComponents();")]
    [InlineData("Program.cs", "app.MapRazorComponents<App>().AddInteractiveServerRenderMode();")]
    [InlineData("Program.cs", "builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();")]
    [InlineData("Components/Routes.razor", "<Router @rendermode=\"InteractiveServer\" />")]
    [InlineData("Components/Pages/Page.razor", "@rendermode InteractiveServer")]
    [InlineData("Components/App.razor", "<Routes @rendermode=\"RenderMode.InteractiveServer\" />")]
    [InlineData("Components/Pages/Page.razor.cs", "static IComponentRenderMode Mode = RenderMode.InteractiveAuto;")]
    [InlineData("Components/Pages/Page.razor.cs", "static object Mode = new InteractiveServerRenderMode();")]
    public void A_deliberate_interactive_render_mode_is_flagged(string relativePath, string text)
    {
        PortalRules.InteractivityViolations([("src/TechStrap.Portal/" + relativePath, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_comment_that_names_the_forbidden_things_is_not_a_hit()
    {
        PortalRules.InteractivityViolations([(Page, "@* No @rendermode here: this site is static server rendering. *@")]).ShouldBeEmpty();
        PortalRules.InteractivityViolations([(Code, "// No AddInteractiveServerComponents: static rendering only.")]).ShouldBeEmpty();
    }

    // ---- The scan ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_scan_skips_build_output_and_includes_every_source_kind()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).Select(f => f.Path.Replace('\\', '/')).ToList();

        files.ShouldNotContain(path => path.Contains("/obj/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal));
        files.ShouldContain("src/TechStrap.Portal/Program.cs");
        files.ShouldContain(path => path.EndsWith("Components/Pages/ProductHome.razor", StringComparison.Ordinal));
        files.ShouldContain(path => path.EndsWith("Components/Pages/Home.razor.cs", StringComparison.Ordinal));
    }
}
