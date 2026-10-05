using AngleSharp.Html.Parser;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The checks both KB profiles share: only allow-listed tags and attributes, no script link, and an image source that is absolute http or https.</summary>
internal static class KbSafeHtml
{
    private static readonly string[] _allowedTags =
    [
        "html", "head", "body", "p", "br", "strong", "b", "em", "i", "u", "a", "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "del", "s", "img", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private static readonly string[] _allowedAttributes = ["href", "src", "alt", "rel", "loading", "referrerpolicy"];

    // An attribute that is allowed on one tag only (the sanitiser adds these itself).
    private static readonly Dictionary<string, string> _attributeOwner = new() { ["rel"] = "a", ["loading"] = "img", ["referrerpolicy"] = "img" };

    public static void ShouldBeSafe(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.All)
        {
            _allowedTags.ShouldContain(element.LocalName, $"tag <{element.LocalName}> survived in: {html}");
            foreach (var attribute in element.Attributes)
            {
                _allowedAttributes.ShouldContain(attribute.Name, $"attribute {attribute.Name} survived in: {html}");
                if (_attributeOwner.TryGetValue(attribute.Name, out var owner))
                {
                    element.LocalName.ShouldBe(owner, $"attribute {attribute.Name} on <{element.LocalName}> in: {html}");
                }
            }

            if (element.LocalName == "a" && element.GetAttribute("href") is { } href)
            {
                href.ShouldNotStartWith("javascript:", Case.Insensitive);
                href.ShouldNotStartWith("data:", Case.Insensitive);
                href.ShouldNotStartWith("vbscript:", Case.Insensitive);
            }

            if (element.LocalName == "img")
            {
                var src = element.GetAttribute("src");
                (src is not null && (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    .ShouldBeTrue($"img src {src} is not http or https in: {html}");
            }
        }
    }
}

/// <summary>
/// The KB sanitiser on its own, fed raw HTML (D-044). Markdig has raw HTML off, so this is the second line of defence: if the Markdown
/// step ever let markup through, these inputs must still come out safe.
/// </summary>
public sealed class KbHtmlSanitizerTests
{
    private readonly KbHtmlSanitizer _sanitizer = new();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"https://ok.example/a.png\" onerror=\"alert(1)\" onload=\"alert(2)\" style=\"x\">",
        "<img src=\"javascript:alert(1)\" alt=\"x\">",
        "<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">",
        "<img src=\"//evil.example/a.png\">",
        "<img src=\"/kb-images/a.png\">",
        "<img src=\"mailto:a@example.com\">",
        "<img srcset=\"https://ok.example/a.png 1x\" src=\"https://ok.example/a.png\">",
        "<svg onload=alert(1)><circle/></svg>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<object data=\"x\"></object>",
        "<embed src=\"x\">",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\" onclick=\"alert(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<table onclick=\"alert(1)\"><tr><td style=\"background:url(javascript:alert(1))\">x</td></tr></table>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<details open ontoggle=alert(1)>x</details>",
        "<style>*{background:url(javascript:alert(1))}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<link rel=\"stylesheet\" href=\"javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_leave_only_allow_listed_tags_attributes_and_web_image_sources(string html) =>
        KbSafeHtml.ShouldBeSafe(_sanitizer.Sanitize(html));

    [Fact]
    public void A_web_image_and_a_table_survive_with_their_allowed_attributes()
    {
        var clean = _sanitizer.Sanitize("<table><thead><tr><th>A</th></tr></thead><tbody><tr><td><img src=\"https://ok.example/a.png\" alt=\"pic\" width=\"9\"></td></tr></tbody></table>");

        clean.ShouldBe("<table><thead><tr><th>A</th></tr></thead><tbody><tr><td><img src=\"https://ok.example/a.png\" alt=\"pic\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></td></tr></tbody></table>");
    }

    [Fact]
    public void Every_link_gets_the_safe_rel() =>
        _sanitizer.Sanitize("<a href=\"https://example.com\" rel=\"opener\">x</a>").ShouldBe("<a href=\"https://example.com\" rel=\"noopener noreferrer nofollow\">x</a>");
}
