using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class MarkdownRendererTests
{
    private readonly MarkdigMarkdownRenderer _markdown = new();
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    private string Render(string markdown) => _sanitizer.Sanitize(_markdown.ToHtml(markdown));

    [Fact]
    public void Common_formatting_survives()
    {
        var html = Render("Hi **Ann**,\nline two\n\n- one\n- two\n\n`code` and ~~old~~\n\n> quoted\n\n---\n\n##### small heading");
        html.ShouldContain("<strong>Ann</strong>");
        html.ShouldContain("<br");
        html.ShouldContain("<ul>");
        html.ShouldContain("<code>code</code>");
        html.ShouldContain("<del>old</del>");
        html.ShouldContain("<blockquote>");
        html.ShouldContain("<hr");
        html.ShouldContain("<h5>");
    }

    [Fact]
    public void Links_get_safe_rel_and_bare_urls_are_linked()
    {
        var html = Render("See [docs](https://example.com/docs) or https://example.com/faq");
        html.ShouldContain("href=\"https://example.com/docs\"");
        html.ShouldContain("href=\"https://example.com/faq\"");
        html.ShouldContain("rel=\"noopener noreferrer nofollow\"");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("<a href=\"https://e.com\" onclick=\"alert(1)\">x</a>")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("<iframe src=\"https://evil\"></iframe>")]
    [InlineData("<style>body{display:none}</style>")]
    public void Raw_html_and_script_links_in_markdown_never_survive_render_and_sanitize(string markdown)
    {
        var html = Render(markdown);
        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldNotContain("javascript:", Case.Insensitive);
        html.ShouldNotContain("onerror", Case.Insensitive);
        html.ShouldNotContain("onclick", Case.Insensitive);
        html.ShouldNotContain("<img", Case.Insensitive);
        html.ShouldNotContain("<iframe", Case.Insensitive);
        html.ShouldNotContain("<style", Case.Insensitive);
        html.ShouldNotContain("data:text/html", Case.Insensitive);
    }

    [Fact]
    public void Raw_html_is_shown_as_text()
    {
        var html = Render("<b>bold?</b>");
        html.ShouldContain("&lt;b&gt;bold?&lt;/b&gt;");
    }

    [Fact]
    public void Empty_and_whitespace_input_render_to_nothing_visible() =>
        Render("   \n  ").Trim().ShouldBeEmpty();
}
