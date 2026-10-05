using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// D-044: the knowledge base got its own content profile, so agent replies (which customers read in email and in the portal) keep
/// the pipeline they had before PHASE-08. These exact outputs were captured from <c>MarkdigMarkdownRenderer</c> and
/// <c>HtmlSanitizerAdapter</c> before the KB profile existed; a change to either shows up here.
/// </summary>
public sealed class MessagePipelineUnchangedTests
{
    private readonly MarkdigMarkdownRenderer _markdown = new();
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    private string Render(string markdown) => _sanitizer.Sanitize(_markdown.ToHtml(markdown));

    [Fact]
    public void An_image_in_a_reply_is_still_removed_so_a_reply_cannot_carry_a_tracking_pixel() =>
        Render("![tracker](https://t.example/p.png)").ShouldBe("<p></p>\n");

    [Fact]
    public void A_pipe_table_in_a_reply_is_still_plain_text_with_line_breaks() =>
        Render("| a | b |\n|---|---|\n| 1 | 2 |").ShouldBe("<p>| a | b |<br>\n|---|---|<br>\n| 1 | 2 |</p>\n");

    [Fact]
    public void A_line_break_in_a_reply_is_still_a_hard_break() =>
        Render("Line one\nLine two").ShouldBe("<p>Line one<br>\nLine two</p>\n");

    [Fact]
    public void Links_in_a_reply_keep_their_rel_and_bare_urls_are_linked() =>
        Render("[docs](https://example.com/docs) and https://example.com/faq").ShouldBe(
            "<p><a href=\"https://example.com/docs\" rel=\"noopener noreferrer nofollow\">docs</a> and "
            + "<a href=\"https://example.com/faq\" rel=\"noopener noreferrer nofollow\">https://example.com/faq</a></p>\n");

    [Fact]
    public void Raw_html_in_a_reply_is_still_escaped() =>
        Render("**bold** and <img src=x onerror=alert(1)>").ShouldBe("<p><strong>bold</strong> and &lt;img src=x onerror=alert(1)&gt;</p>\n");

    [Fact]
    public void Headings_lists_and_code_in_a_reply_are_unchanged() =>
        Render("# Title\n\n- one\n- two\n\n`code`").ShouldBe("<h1>Title</h1>\n<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n<p><code>code</code></p>\n");
}
