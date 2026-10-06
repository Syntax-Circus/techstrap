using System.Reflection;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>P09-T01: every route template and builder lives in <see cref="PortalRoutes"/>, so a page never repeats a route string.</summary>
public sealed class PortalRoutesTests
{
    private static IEnumerable<(string Name, string Value)> Templates() =>
        typeof(PortalRoutes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name.EndsWith("Template", StringComparison.Ordinal))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_template_is_an_absolute_path_and_no_two_are_equal()
    {
        var templates = Templates().ToList();

        templates.Count.ShouldBe(15, "a route was added or removed: update the spec's route list and this count together");
        templates.ShouldAllBe(t => t.Value.StartsWith('/'));
        templates.Select(t => t.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(templates.Count);
    }

    [Fact]
    public void The_help_centre_segments_and_query_names_are_the_ones_the_cache_and_the_pages_share()
    {
        PortalRoutes.KbSegment.ShouldBe("kb");
        PortalRoutes.KbSearchSegment.ShouldBe("search");
        PortalRoutes.PageParameter.ShouldBe("page");
        PortalRoutes.QueryParameter.ShouldBe("q");
        PortalRoutes.KbHome("paperplane").ShouldBe("/p/paperplane/kb");
        PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
    }

    [Fact]
    public void The_templates_match_the_routes_of_the_spec_exactly()
    {
        PortalRoutes.HomeTemplate.ShouldBe("/");
        PortalRoutes.NotFoundTemplate.ShouldBe("/not-found");
        PortalRoutes.ErrorTemplate.ShouldBe("/error");
        PortalRoutes.StyleGuideTemplate.ShouldBe("/_styleguide");
        PortalRoutes.ProductHomeTemplate.ShouldBe("/p/{key}");
        PortalRoutes.ContactTemplate.ShouldBe("/p/{key}/contact");
        PortalRoutes.ContactReceivedTemplate.ShouldBe("/p/{key}/contact/received");
        PortalRoutes.LostLinkTemplate.ShouldBe("/p/{key}/lost-link");
        PortalRoutes.KbHomeTemplate.ShouldBe("/p/{key}/kb");
        PortalRoutes.KbCategoryTemplate.ShouldBe("/p/{key}/kb/{category}");
        PortalRoutes.KbArticleTemplate.ShouldBe("/p/{key}/kb/{category}/{slug}");
        PortalRoutes.KbSearchTemplate.ShouldBe("/p/{key}/kb/search");
        PortalRoutes.SuggestTemplate.ShouldBe("/p/{key}/suggest");
        PortalRoutes.TicketTemplate.ShouldBe("/t/{token}");
        PortalRoutes.TicketAttachmentTemplate.ShouldBe("/t/{token}/attachments/{id}");
    }

    [Fact]
    public void The_builders_fill_the_templates()
    {
        PortalRoutes.ProductHome("paperplane").ShouldBe("/p/paperplane");
        PortalRoutes.Contact("paperplane").ShouldBe("/p/paperplane/contact");
        PortalRoutes.ContactReceived("paperplane").ShouldBe("/p/paperplane/contact/received");
        PortalRoutes.LostLink("paperplane").ShouldBe("/p/paperplane/lost-link");
        PortalRoutes.KbHome("paperplane").ShouldBe("/p/paperplane/kb");
        PortalRoutes.KbCategory("paperplane", "guides").ShouldBe("/p/paperplane/kb/guides");
        PortalRoutes.KbArticle("paperplane", "guides", "dark-mode").ShouldBe("/p/paperplane/kb/guides/dark-mode");
        PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
        PortalRoutes.Suggest("paperplane").ShouldBe("/p/paperplane/suggest");
        PortalRoutes.Ticket("abc").ShouldBe("/t/abc");
        PortalRoutes.TicketAttachment("abc", Guid.Parse("11111111-2222-3333-4444-555555555555")).ShouldBe("/t/abc/attachments/11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void The_suggest_route_is_beside_the_kb_not_under_it_so_no_category_slug_can_ever_shadow_it()
    {
        // D-045 addendum (2026-10-06): /p/{key}/kb/suggest would have made a category called "suggest" unreachable.
        PortalRoutes.SuggestTemplate.ShouldBe("/p/{key}/suggest");
        PortalRoutes.Suggest("paperplane").ShouldBe("/p/paperplane/suggest");
    }

    [Fact]
    public void A_builder_escapes_each_value_so_it_can_never_add_a_segment_a_query_or_a_fragment()
    {
        PortalRoutes.ProductHome("a/b?c#d").ShouldBe("/p/a%2Fb%3Fc%23d");
        PortalRoutes.KbArticle("p", "../x", "y z").ShouldBe("/p/p/kb/..%2Fx/y%20z");
        PortalRoutes.Ticket("a b").ShouldBe("/t/a%20b");
    }

    [Fact]
    public void The_lost_link_confirmation_is_the_lost_link_page_with_the_sent_flag_and_nothing_about_an_address()
    {
        PortalRoutes.LostLinkSent("paperplane").ShouldBe("/p/paperplane/lost-link?sent=1");
        PortalRoutes.SentParameter.ShouldBe("sent");
    }

    [Fact]
    public void The_received_page_builder_adds_the_reference_escaped_so_it_can_never_add_a_parameter()
    {
        PortalRoutes.ContactReceived("paperplane", "CfDJ8_a-b").ShouldBe("/p/paperplane/contact/received?ref=CfDJ8_a-b");
        PortalRoutes.ContactReceived("paperplane", "a&b=c#d e").ShouldBe("/p/paperplane/contact/received?ref=a%26b%3Dc%23d%20e");
        PortalRoutes.ReceivedReferenceParameter.ShouldBe("ref");
    }

    [Fact]
    public void A_builder_given_a_ticket_token_uses_its_real_value_and_never_the_printed_marker()
    {
        const string text = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
        TicketToken.TryParse(text, out var token).ShouldBeTrue();
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        PortalRoutes.Ticket(token).ShouldBe($"/t/{text}");
        PortalRoutes.TicketAttachment(token, id).ShouldBe($"/t/{text}/attachments/{id}");
        PortalRoutes.Ticket(token).ShouldNotContain("[token]");
        PortalRoutes.TicketAttachment(token, id).ShouldNotContain("[token]");
    }

    [Fact]
    public void The_prefixes_cover_the_product_and_ticket_routes()
    {
        PortalRoutes.ProductPrefix.ShouldBe("/p");
        PortalRoutes.TicketPrefix.ShouldBe("/t");
        Templates().Where(t => t.Name.StartsWith("Ticket", StringComparison.Ordinal)).ShouldAllBe(t => t.Value.StartsWith("/t/", StringComparison.Ordinal));
    }
}
