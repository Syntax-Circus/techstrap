using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests.Components;

/// <summary>P12-T05: the Admin's one MarkupString site shows the sanitised body as given and adds nothing; every other string on the bubble is encoded by Razor.</summary>
public sealed class MessageBubbleBodyTests : AdminComponentTest
{
    public static TheoryData<string> SanitisedBodies() =>
    [
        "<p>Hello <a href=\"https://x.example\" rel=\"noopener noreferrer nofollow\">link</a></p>",
        "<h2>Heading</h2><ul><li>one</li><li><strong>two</strong></li></ul>",
        "<p>&lt;script&gt;alert(1)&lt;/script&gt; and &lt;img src=x onerror=alert(1)&gt; stay text</p>",
    ];

    [Theory]
    [MemberData(nameof(SanitisedBodies))]
    public void A_sanitised_body_is_rendered_inside_the_message_body_div_with_nothing_added(string body)
    {
        var message = new MessageViewModel(
            Guid.NewGuid(), EntryKind.Customer, "Ada <script>alert(1)</script>", DateTimeOffset.UnixEpoch, body,
            [new AttachmentDto(Guid.NewGuid(), "<script>alert(2)</script>.png", "image/png", 10)],
            [new LinkedArticleDto(Guid.NewGuid(), "<script>alert(3)</script>", "slug")]);

        var cut = Render<MessageBubble>(parameters => parameters.Add(bubble => bubble.Message, message));

        cut.Find(".ts-message-body").InnerHtml.ShouldBe(body);
        cut.FindAll(".ts-message-body script").ShouldBeEmpty();
        cut.Markup.ShouldContain("&lt;script&gt;alert(2)&lt;/script&gt;.png");
        cut.Markup.ShouldContain("&lt;script&gt;alert(3)&lt;/script&gt;");
        XssAssertions.ShouldHaveNoActiveContent(cut.Markup, "the message bubble");
    }
}
