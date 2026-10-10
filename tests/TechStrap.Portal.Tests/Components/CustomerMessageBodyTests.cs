using Bunit;
using TechStrap.Portal.Components.Tickets;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Components;

/// <summary>P12-T05: the Portal's one MarkupString site shows the API's sanitized body with only the h1 demoted to h2, and adds nothing.</summary>
public sealed class CustomerMessageBodyTests : BunitContext
{
    [Theory]
    [InlineData("<p>Hello <a href=\"https://x.example\" rel=\"noopener noreferrer nofollow\">link</a></p>", "<p>Hello <a href=\"https://x.example\" rel=\"noopener noreferrer nofollow\">link</a></p>")]
    [InlineData("<h1>Title</h1><p>Text</p>", "<h2>Title</h2><p>Text</p>")]
    [InlineData("<p>&lt;script&gt;alert(1)&lt;/script&gt; stays text</p>", "<p>&lt;script&gt;alert(1)&lt;/script&gt; stays text</p>")]
    public void A_sanitised_body_is_rendered_with_headings_demoted_and_nothing_added(string body, string expected)
    {
        var cut = Render<CustomerMessageBody>(parameters => parameters.Add(component => component.Html, body));

        cut.Find(".ts-message-body").InnerHtml.ShouldBe(expected);
        cut.FindAll("h1").ShouldBeEmpty();
        XssAssertions.ShouldHaveNoActiveContent(cut.Markup, "the customer message body");
    }
}
