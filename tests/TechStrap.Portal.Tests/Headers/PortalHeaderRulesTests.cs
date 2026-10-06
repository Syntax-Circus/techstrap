using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Headers;

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

    [Fact]
    public void The_rules_are_the_ticket_headers_and_the_attachment_sandbox_and_nothing_else()
    {
        PortalHeaderRules.Rules.Count.ShouldBe(2);
        PortalHeaderRules.Rules[0].Matches(new PathString("/t/x")).ShouldBeTrue();
        PortalHeaderRules.Rules[0].Matches(new PathString("/p/x")).ShouldBeFalse();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token + "/attachments/" + Id)).ShouldBeTrue();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token)).ShouldBeFalse();
    }
}
