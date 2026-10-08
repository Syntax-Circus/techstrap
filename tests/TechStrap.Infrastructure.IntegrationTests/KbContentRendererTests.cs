using TechStrap.Tests.Shared;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// D-021, D-044: the KB content profile. Review Focus 1 (stored XSS through KB Markdown or the preview): whatever an agent types, the
/// output holds only allow-listed tags and attributes, and an image keeps only an absolute http or https source.
/// </summary>
public sealed class KbContentRendererTests
{
    private readonly KbContentRenderer _renderer = new();

    // The refusal itself is pinned by the content assertions (no list or table markup): without the cap they fail. This ceiling
    // only catches a gross hang. It is loose because shared CI runners execute test projects in parallel, and a 2-second wall clock
    // there failed a capped render that took 2.1 s; the uncapped 50k-item render takes about 6-7 s on a developer machine.
    private static readonly TimeSpan RenderCeiling = TimeSpan.FromSeconds(10);

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<details open ontoggle=alert(1)>x</details>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "[x](javascript:alert(1))",
        "[x](JaVaScRiPt:alert(1))",
        "[x](&#106;avascript:alert(1))",
        "[x](jav&#x09;ascript:alert(1))",
        "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)",
        "[x](vbscript:msgbox(1))",
        "![x](javascript:alert(1))",
        "![x](data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+)",
        "![x](data:image/png;base64,AAAA)",
        "![x](//evil.example/a.png)",
        "![x](/kb-images/a.png)",
        "![x](kb-images/a.png)",
        "![x](mailto:a@example.com)",
        "![x](ftp://evil.example/a.png)",
        "![x](file:///etc/passwd)",
        "![x](https://ok.example/a.png \"t\" onerror=\"alert(1)\")",
        "![x\" onerror=\"alert(1)](https://ok.example/a.png)",
        "![](https://ok.example/a.png)<img src=x onerror=alert(1)>",
        "| a | b |\n|---|---|\n| <script>alert(1)</script> | <img src=x onerror=alert(1)> |",
        "| a |\n|---|\n| [x](javascript:alert(1)) |",
        "<table><tr><td onclick=alert(1)>x</td></tr></table>",
        "<style>*{background:url(javascript:alert(1))}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_leave_only_allow_listed_tags_attributes_and_web_image_sources(string markdown)
    {
        KbSafeHtml.ShouldBeSafe(_renderer.Render(markdown));
    }

    [Fact]
    public void An_https_image_keeps_its_source_and_alt_and_gets_lazy_loading_and_no_referrer()
    {
        var html = _renderer.Render("![Router front](https://api.example.com/kb-images/0199.png)");

        html.ShouldBe("<p><img src=\"https://api.example.com/kb-images/0199.png\" alt=\"Router front\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></p>\n");
    }

    [Theory]
    [InlineData("![x](/kb-images/a.png)")]
    [InlineData("![x](//evil.example/a.png)")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("![x](data:image/png;base64,AAAA)")]
    [InlineData("![x](mailto:a@example.com)")]
    public void An_image_with_any_other_source_is_removed_entirely(string markdown) =>
        _renderer.Render(markdown).ShouldNotContain("<img");

    [Fact]
    public void A_pipe_table_renders_as_a_table()
    {
        var html = _renderer.Render("| Name | Port |\n|------|------|\n| smtp | 587 |");

        html.ShouldContain("<table>");
        html.ShouldContain("<th>Name</th>");
        html.ShouldContain("<td>587</td>");
    }

    [Fact]
    public void Raw_html_is_shown_as_text_never_as_markup()
    {
        var html = _renderer.Render("Use <b onclick=x>bold</b> here");

        html.ShouldBe("<p>Use &lt;b onclick=x&gt;bold&lt;/b&gt; here</p>\n");
    }

    [Fact]
    public void Links_get_the_safe_rel_and_bare_urls_are_linked()
    {
        var html = _renderer.Render("See [docs](https://example.com/docs) or https://example.com/faq");

        html.ShouldContain("href=\"https://example.com/docs\" rel=\"noopener noreferrer nofollow\"");
        html.ShouldContain("href=\"https://example.com/faq\" rel=\"noopener noreferrer nofollow\"");
    }

    [Fact]
    public void A_soft_line_break_stays_a_space_in_long_form_text()
    {
        _renderer.Render("one\ntwo").ShouldBe("<p>one\ntwo</p>\n");
    }

    [Fact]
    public void Pathological_nesting_falls_back_to_encoded_text()
    {
        var html = _renderer.Render(new string('[', 200) + "<script>alert(1)</script>");

        html.ShouldNotContain("<script");
        html.ShouldStartWith("<p>");
    }

    private static string PipeTable(int rows) =>
        "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Repeat("| 1 | 2 |\n", rows));

    [Fact]
    public void A_200k_character_pipe_table_is_refused_before_sanitising_and_is_fast()
    {
        var markdown = PipeTable(20_000);
        markdown.Length.ShouldBeGreaterThanOrEqualTo(190_000);

        var tooComplex = _renderer.IsTooComplex(markdown);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var html = _renderer.Render(markdown);
        watch.Stop();

        tooComplex.ShouldBeTrue();
        html.ShouldNotContain("<table");
        watch.Elapsed.ShouldBeLessThan(RenderCeiling);
    }

    [Fact]
    public void A_list_of_50k_items_is_refused_before_sanitising_and_is_fast()
    {
        var markdown = string.Concat(Enumerable.Repeat("- x\n", 50_000));

        var tooComplex = _renderer.IsTooComplex(markdown);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var html = _renderer.Render(markdown);
        watch.Stop();

        tooComplex.ShouldBeTrue();
        html.ShouldNotContain("<li>");
        watch.Elapsed.ShouldBeLessThan(RenderCeiling);
    }

    [Fact]
    public void A_legitimate_table_under_the_cap_still_renders_as_a_table_quickly()
    {
        var markdown = PipeTable(600);

        var tooComplex = _renderer.IsTooComplex(markdown);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var html = _renderer.Render(markdown);
        watch.Stop();

        tooComplex.ShouldBeFalse();
        html.ShouldContain("<table>");
        html.ShouldContain("<td>2</td>");
        watch.Elapsed.ShouldBeLessThan(RenderCeiling);
    }

    [Fact]
    public void An_over_the_cap_body_is_shown_as_encoded_text_never_as_markup()
    {
        var html = _renderer.Render(PipeTable(20_000) + "<script>alert(1)</script>");

        html.ShouldStartWith("<p>");
        html.ShouldNotContain("<script");
        html.ShouldNotContain("<table");
    }

    [Fact]
    public void An_empty_source_is_not_too_complex() => _renderer.IsTooComplex(string.Empty).ShouldBeFalse();

    [Fact]
    public void An_empty_source_renders_nothing()
    {
        _renderer.Render(string.Empty).ShouldBeEmpty();
    }

    public static IEnumerable<TheoryDataRow<string>> CorpusRows() => XssCorpus.Rows();

    // The renderer has one entry point: raw HTML typed in the Markdown source is the same string, so the vector is rendered as a
    // whole document and after a heading (where it sits in a later block).
    [Theory]
    [MemberData(nameof(CorpusRows))]
    public void Every_corpus_vector_renders_inert_as_markdown_and_as_html(string vector)
    {
        XssAssertions.ShouldHaveNoActiveContent(_renderer.Render(vector), vector);
        XssAssertions.ShouldHaveNoActiveContent(_renderer.Render("# Title\n\n" + vector),"# Title + " + vector);
    }
}
