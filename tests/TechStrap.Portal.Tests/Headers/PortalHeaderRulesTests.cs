using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Headers;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>The two path predicates behind the Portal's header rules: everything under <c>/t</c>, and only the attachment route under it.</summary>
public sealed class PortalHeaderRulesTests
{
    private const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    private const string Id = "11111111-2222-3333-4444-555555555555";

    [Theory]
    [InlineData("/t")]
    [InlineData("/t/")]
    [InlineData("/T")]
    [InlineData("/t/" + Token)]
    [InlineData("/T/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments/" + Id)]
    [InlineData("/t/" + Token + "/anything/at/all")]
    [InlineData("/t//" + Token)]
    public void A_path_under_t_is_a_ticket_path(string path) => PortalHeaderRules.IsTicketPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/")]
    [InlineData("/tx")]
    [InlineData("/t-x")]
    [InlineData("/ticket")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/t/x")]
    [InlineData("/not-found")]
    [InlineData("/robots.txt")]
    [InlineData("")]
    public void Any_other_path_is_not(string path) => PortalHeaderRules.IsTicketPath(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/t/" + Token + "/attachments/" + Id)]
    [InlineData("/T/" + Token + "/ATTACHMENTS/" + Id)]
    [InlineData("/t/" + Token + "/attachments/" + Id + "/")]
    [InlineData("/t/" + Token + "/attachments/not-a-guid")]
    public void The_attachment_route_under_t_is_an_attachment_path(string path) => PortalHeaderRules.IsTicketAttachmentPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/t")]
    [InlineData("/t/" + Token)]
    [InlineData("/t/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments")]
    [InlineData("/t/" + Token + "/attachments/")]
    [InlineData("/t/" + Token + "/attachments/" + Id + "/more")]
    [InlineData("/t/" + Token + "/files/" + Id)]
    [InlineData("/t/attachments/" + Id)]
    [InlineData("/attachments/" + Id)]
    [InlineData("/p/paperplane/attachments/" + Id)]
    public void The_ticket_page_and_everything_else_is_not(string path) => PortalHeaderRules.IsTicketAttachmentPath(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/")]
    [InlineData("/P/Paperplane/CONTACT")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/contact/received/")]
    [InlineData("/p/paperplane/Contact/Received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/p/paperplane/LOST-LINK/")]
    [InlineData("/p/paperplane/suggest")]
    [InlineData("/p/paperplane/Suggest/")]
    [InlineData("/p//paperplane/contact")]
    public void The_four_form_pages_of_a_product_are_form_pages(string path) => PortalHeaderRules.IsFormPagePath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/")]
    [InlineData("/p")]
    [InlineData("/p/")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/contact")]
    [InlineData("/p/paperplane/contact/extra")]
    [InlineData("/p/paperplane/contact/received/more")]
    [InlineData("/p/paperplane/received")]
    [InlineData("/p/paperplane/lost-link/more")]
    [InlineData("/p/paperplane/lostlink")]
    [InlineData("/p/paperplane/suggest/more")]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/suggest")]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/p/paperplane/kb/guides/contact")]
    [InlineData("/contact")]
    [InlineData("/t/x/contact")]
    [InlineData("/pp/paperplane/contact")]
    [InlineData("")]
    public void The_product_home_the_kb_and_every_other_path_are_not(string path) => PortalHeaderRules.IsFormPagePath(path).ShouldBeFalse(path);

    [Fact]
    public void The_segments_of_the_form_pages_are_the_ones_the_route_templates_end_with()
    {
        PortalRoutes.ContactTemplate.ShouldEndWith("/" + PortalRoutes.ContactSegment);
        PortalRoutes.ContactReceivedTemplate.ShouldEndWith("/" + PortalRoutes.ContactSegment + "/" + PortalRoutes.ReceivedSegment);
        PortalRoutes.LostLinkTemplate.ShouldEndWith("/" + PortalRoutes.LostLinkSegment);
        PortalRoutes.SuggestTemplate.ShouldEndWith("/" + PortalRoutes.SuggestSegment);
    }

    [Fact]
    public void The_rules_are_the_ticket_headers_the_attachment_sandbox_and_the_form_page_headers_and_nothing_else()
    {
        PortalHeaderRules.Rules.Count.ShouldBe(3);
        PortalHeaderRules.Rules[2].Matches(new PathString("/p/x/contact")).ShouldBeTrue();
        PortalHeaderRules.Rules[2].Matches(new PathString("/p/x")).ShouldBeFalse();
        PortalHeaderRules.Rules[2].Matches(new PathString("/t/x")).ShouldBeFalse();
        PortalHeaderRules.Rules[0].Matches(new PathString("/t/x")).ShouldBeTrue();
        PortalHeaderRules.Rules[0].Matches(new PathString("/p/x")).ShouldBeFalse();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token + "/attachments/" + Id)).ShouldBeTrue();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token)).ShouldBeFalse();
    }
}
