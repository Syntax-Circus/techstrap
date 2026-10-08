namespace TechStrap.Architecture.Tests;

public sealed class PortalLinkRuleTests
{
    [Theory]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor", "<a href=\"@PortalRoutes.KbArticle(key, c, s)\">x</a>")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "var link = PortalRoutes.Contact(Key);")]
    [InlineData("src/TechStrap.Portal/Seo/Thing.cs", "var url = PortalRoutes.ProductHome(key);")]
    public void A_component_or_Seo_file_that_calls_a_link_builder_is_flagged(string path, string text)
    {
        PortalRules.LinkBuilderViolations([(path, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_route_template_and_a_constant_are_not_a_link_builder()
    {
        PortalRules.LinkBuilderViolations(
        [
            ("src/TechStrap.Portal/Components/Pages/KbArticle.razor", "@attribute [Route(PortalRoutes.KbArticleTemplate)]"),
            ("src/TechStrap.Portal/Seo/Robots.cs", "var d = $\"Disallow: {PortalRoutes.TicketPrefix}/\"; var p = PortalRoutes.PageParameter;"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "// see PortalRoutes.KbSearch(string, string, int)"),
        ]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("src/TechStrap.Portal/Routing/PortalLinks.cs")]
    [InlineData("src/TechStrap.Portal/Routing/PageLinks.cs")]
    [InlineData("src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs")]
    [InlineData("src/TechStrap.Portal/Hosting/ProductHostMiddleware.cs")]
    public void The_link_service_and_code_outside_components_and_Seo_may_call_a_builder(string path)
    {
        PortalRules.LinkBuilderViolations([(path, "var link = PortalRoutes.KbArticle(key, c, s);")]).ShouldBeEmpty();
    }

    [Fact]
    public void No_Portal_component_or_Seo_file_calls_a_PortalRoutes_link_builder()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("Routing/PortalLinks.cs", StringComparison.Ordinal) && f.Text.Contains("PortalRoutes.KbArticle(", StringComparison.Ordinal), "the one place that may call a builder must be in the scan");
        PortalRules.LinkBuilderViolations(files).ShouldBeEmpty();
    }
}
