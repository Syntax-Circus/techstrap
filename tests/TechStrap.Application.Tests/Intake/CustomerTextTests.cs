using TechStrap.Application.Intake;

namespace TechStrap.Application.Tests.Intake;

public sealed class CustomerTextTests
{
    [Fact]
    public void Pasted_markup_is_shown_as_text_not_rendered() =>
        CustomerText.ToHtml("<script>alert(1)</script> & <b>bold</b>")
            .ShouldBe("<p>&lt;script&gt;alert(1)&lt;/script&gt; &amp; &lt;b&gt;bold&lt;/b&gt;</p>");

    [Fact]
    public void Blank_lines_separate_paragraphs_and_single_newlines_become_breaks() =>
        CustomerText.ToHtml("Hi,\r\nit broke.\n\nThanks").ShouldBe("<p>Hi,<br>it broke.</p><p>Thanks</p>");

    [Fact]
    public void Surrounding_whitespace_and_extra_blank_lines_are_ignored() =>
        CustomerText.ToHtml("\n\n  one  \n\n\n\ntwo\n").ShouldBe("<p>one</p><p>two</p>");
}
