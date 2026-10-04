using AngleSharp.Html.Parser;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class MarkdownRendererTests
{
    private static readonly string[] _forbiddenTags = ["script", "img", "iframe", "style", "object", "embed"];

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
        var doc = new HtmlParser().ParseDocument("<body>" + Render(markdown) + "</body>");
        var elements = doc.Body!.QuerySelectorAll("*").ToList();

        elements.Where(e => _forbiddenTags.Contains(e.LocalName)).ShouldBeEmpty();
        elements.SelectMany(e => e.Attributes).Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
        foreach (var attr in elements.SelectMany(e => e.Attributes).Where(a => a.Name is "href" or "src"))
        {
            attr.Value.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
            attr.Value.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }

        foreach (var a in doc.Body.QuerySelectorAll("a[href]"))
        {
            new[] { "http:", "https:", "mailto:" }.Any(x => a.GetAttribute("href")!.StartsWith(x, StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
        }
    }

    [Fact]
    public void Escaped_html_keeps_the_agents_text()
    {
        var doc = new HtmlParser().ParseDocument("<body>" + Render("<img src=x onerror=alert(1)>") + "</body>");
        doc.Body!.TextContent.ShouldContain("onerror");
    }

    [Fact]
    public void The_renderer_alone_leaves_no_live_img_for_raw_html()
    {
        var doc = new HtmlParser().ParseDocument("<body>" + _markdown.ToHtml("<img src=x onerror=alert(1)>") + "</body>");

        doc.Body!.QuerySelectorAll("img").ShouldBeEmpty();
    }

    public static TheoryData<string, string> DeeplyNestedInputs() => new()
    {
        { string.Concat(Enumerable.Repeat("x\u001b[0m y ", 150)), "x\u001b[0m y" },
        { "values: " + string.Concat(Enumerable.Repeat("[1, ", 150)), "values: [1, [1, " },
        { new string('>', 128) + " quoted", "quoted" },
    };

    [Theory]
    [MemberData(nameof(DeeplyNestedInputs))]
    public void Pathologically_nested_text_falls_back_to_encoded_plain_text(string markdown, string expectedFragment)
    {
        var html = Should.NotThrow(() => Render(markdown));

        new HtmlParser().ParseDocument("<body>" + html + "</body>").Body!.TextContent.ShouldContain(expectedFragment);
    }

    [Fact]
    public void Fallback_html_encodes_the_agents_text()
    {
        var html = _markdown.ToHtml(string.Concat(Enumerable.Repeat("[", 200)) + "<b>x</b> & y");

        html.ShouldContain("&lt;b&gt;x&lt;/b&gt; &amp; y");
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
