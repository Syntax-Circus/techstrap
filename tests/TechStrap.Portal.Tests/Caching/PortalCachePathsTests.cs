using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Caching;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// PHASE-09c Review Focus 3 (cache safety): the one predicate that decides what the Portal keeps. Only the three kinds of help-centre page; never the search page, the form pages, the suggest adapter, <c>/t/*</c> or a
/// request whose <c>page</c> value could fill the store.
/// </summary>
public sealed class PortalCachePathsTests
{
    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/")]
    [InlineData("/P/Paperplane/KB")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/")]
    [InlineData("/p/paperplane/kb/accounts/reset-password")]
    [InlineData("/p/paperplane/KB/Accounts/Reset-Password/")]
    [InlineData("/p//paperplane/kb")]
    [InlineData("/p/paperplane/kb/searching")]
    [InlineData("/p/paperplane/kb/accounts/search")]
    public void The_help_centre_home_a_category_and_an_article_are_kb_pages(string path) => PortalCachePaths.IsKbPage(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/")]
    [InlineData("/p")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/p/paperplane/kb/SEARCH")]
    [InlineData("/p/paperplane/kb/search/")]
    [InlineData("/p/paperplane/kb/search/anything")]
    [InlineData("/p/paperplane/kb/accounts/reset-password/extra")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/p/paperplane/suggest")]
    [InlineData("/p/paperplane/kbx")]
    [InlineData("/p/kb")]
    [InlineData("/t/x")]
    [InlineData("/t/kb/accounts")]
    [InlineData("/kb")]
    [InlineData("/pp/paperplane/kb")]
    [InlineData("/not-found")]
    [InlineData("/error")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("")]
    public void The_product_home_the_search_page_the_forms_tickets_and_every_other_path_are_not(string path) => PortalCachePaths.IsKbPage(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/P/Paperplane/KB/Search/")]
    public void Exactly_the_search_page_is_the_search_path(string path) => PortalCachePaths.IsKbSearchPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/search/more")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/search")]
    [InlineData("/t/kb/search")]
    public void Nothing_else_is_the_search_path(string path) => PortalCachePaths.IsKbSearchPath(path).ShouldBeFalse(path);

    private static bool Cacheable(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return PortalCachePaths.IsCacheable(context.Request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?page=2")]
    [InlineData("?page=9999")]
    [InlineData("?PAGE=7")]
    [InlineData("?utm_source=mail&utm_campaign=x")]
    [InlineData("?page=3&utm=1")]
    public void A_category_with_no_page_value_or_a_plain_page_number_is_kept(string query) => Cacheable("/p/paperplane/kb/accounts", query).ShouldBeTrue(query);

    [Theory]
    [InlineData("?page=")]
    [InlineData("?page=abc")]
    [InlineData("?page=1")]
    [InlineData("?page=0")]
    [InlineData("?page=01")]
    [InlineData("?page=-1")]
    [InlineData("?page=+1")]
    [InlineData("?page=10000")]
    [InlineData("?page=99999999999")]
    [InlineData("?page=1.5")]
    [InlineData("?page=1%200")]
    [InlineData("?page=1&page=2")]
    [InlineData("?page=1&PAGE=1")]
    [InlineData("?page")]
    public void A_page_value_that_is_not_two_to_four_digits_is_never_kept_so_it_cannot_fill_the_store(string query) => Cacheable("/p/paperplane/kb/accounts", query).ShouldBeFalse(query);

    [Theory]
    [InlineData("/p/PAPERPLANE/kb")]
    [InlineData("/p/paperplane/KB")]
    [InlineData("/P/paperplane/kb")]
    [InlineData("/p/paperplane/kb/ACCOUNTS")]
    [InlineData("/p/paperplane/kb/accounts/Reset-password")]
    public void A_path_with_an_upper_case_letter_is_never_kept_so_it_can_never_be_answered_from_the_lower_case_entry(string path) => Cacheable(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/p/paperplane/kb", "")]
    [InlineData("/p/paperplane/kb", "?utm=1")]
    [InlineData("/p/paperplane/kb/accounts/reset-password", "")]
    public void The_home_and_an_article_are_kept_with_no_page_value(string path, string query) => Cacheable(path, query).ShouldBeTrue(path + query);

    [Theory]
    [InlineData("/p/paperplane/kb", "?page=2")]
    [InlineData("/p/paperplane/kb", "?page=5")]
    [InlineData("/p/paperplane/kb", "?page=1")]
    [InlineData("/p/paperplane/kb/accounts/reset-password", "?page=2")]
    [InlineData("/p/paperplane/kb/accounts/reset-password", "?page=1")]
    public void On_the_home_and_an_article_any_page_value_is_never_kept_because_those_pages_do_not_page(string path, string query) => Cacheable(path, query).ShouldBeFalse(path + query);

    [Theory]
    [InlineData("/p/paperplane/kb/search", "?q=router")]
    [InlineData("/p/paperplane/contact", "")]
    [InlineData("/p/paperplane/suggest", "?q=a")]
    [InlineData("/t/abc", "")]
    [InlineData("/p/paperplane", "")]
    public void A_path_that_is_not_a_kb_page_is_never_kept_whatever_its_query(string path, string query) => Cacheable(path, query).ShouldBeFalse(path + query);

    [Fact]
    public void The_server_and_the_browser_keep_a_page_for_the_same_minute()
    {
        PortalCachePaths.Lifetime.ShouldBe(TimeSpan.FromSeconds(60));
        PortalCachePaths.BrowserCacheControl.ShouldBe($"public, max-age={(int)PortalCachePaths.Lifetime.TotalSeconds}");
    }

    [Fact]
    public void The_segments_are_the_ones_the_route_templates_use()
    {
        PortalRoutes.KbHomeTemplate.ShouldEndWith("/" + PortalRoutes.KbSegment);
        PortalRoutes.KbSearchTemplate.ShouldEndWith("/" + PortalRoutes.KbSegment + "/" + PortalRoutes.KbSearchSegment);
        PortalRoutes.KbSearchSegment.ShouldBe(TechStrap.Contracts.Kb.KbLimits.ReservedCategorySlug, "the API reserves the slug the search page uses, so no category can shadow it");
    }
}
