using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class HtmlSanitizerTests
{
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<object data=\"x\"></object>",
        "<embed src=\"x\">",
        "<body onload=alert(1)>",
        "<div style=\"background:url(javascript:alert(1))\">x</div>",
        "<style>*{background:url('javascript:alert(1)')}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<input autofocus onfocus=alert(1)>",
        "<details open ontoggle=alert(1)>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\">",
        "<link rel=\"stylesheet\" href=\"javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_lose_every_script_and_handler(string html)
    {
        var clean = _sanitizer.Sanitize(html).ToLowerInvariant();

        clean.ShouldNotContain("<script");
        clean.ShouldNotContain("javascript:");
        clean.ShouldNotContain("vbscript:");
        clean.ShouldNotContain("data:");
        clean.ShouldNotContain("onerror");
        clean.ShouldNotContain("onload");
        clean.ShouldNotContain("onfocus");
        clean.ShouldNotContain("ontoggle");
        clean.ShouldNotContain("<iframe");
        clean.ShouldNotContain("<object");
        clean.ShouldNotContain("<embed");
        clean.ShouldNotContain("<meta");
        clean.ShouldNotContain("<base");
        clean.ShouldNotContain("<form");
    }

    [Fact]
    public void Safe_formatting_is_kept_and_links_get_safe_rel() =>
        _sanitizer.Sanitize("<p>Hi <strong>there</strong> <a href=\"https://example.com\">docs</a></p>")
            .ShouldSatisfyAllConditions(
                clean => clean.ShouldContain("<strong>there</strong>"),
                clean => clean.ShouldContain("href=\"https://example.com\""),
                clean => clean.ShouldContain("rel=\"noopener noreferrer nofollow\""));

    [Fact]
    public void Customer_text_survives_sanitizing_unchanged()
    {
        var html = TechStrap.Application.Intake.CustomerText.ToHtml("Line one\nLine <two>");

        _sanitizer.Sanitize(html).ShouldBe(html);
    }
}
