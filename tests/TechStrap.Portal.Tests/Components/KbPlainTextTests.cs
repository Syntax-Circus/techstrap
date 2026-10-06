using TechStrap.Portal.Components.Kb;

namespace TechStrap.Portal.Tests.Components;

/// <summary>PHASE-09c T14, ruling 5: the description of an article is its summary, else the first sentence of its body as plain text. The result is text that a meta tag encodes, never markup.</summary>
public sealed class KbPlainTextTests
{
    [Theory]
    [InlineData("How to reset it", "<p>Ignored.</p>", "How to reset it")]
    [InlineData("  How   to\nreset  it  ", null, "How to reset it")]
    [InlineData("Summary with <b>tags</b> stays as written", "<p>x</p>", "Summary with <b>tags</b> stays as written")]
    public void A_summary_with_words_is_the_description(string summary, string? html, string expected) => KbPlainText.Describe(summary, html).ShouldBe(expected);

    [Theory]
    [InlineData(null, "<p>Open the app. Then tap settings.</p>", "Open the app.")]
    [InlineData("", "<p>Open the app. Then tap settings.</p>", "Open the app.")]
    [InlineData("   ", "<h1>Title</h1>\n<p>Open the app! Then go.</p>", "Open the app!")]
    [InlineData(null, "<h1>Reset</h1>\n<p>Is it <strong>broken</strong>? Try this.</p>", "Is it broken?")]
    [InlineData(null, "<p>No full stop here</p>", "No full stop here")]
    [InlineData(null, "<p>Fish &amp; chips &lt;3. Next.</p>", "Fish & chips <3.")]
    [InlineData(null, "<p class=\"lead\" id=\"x\">Attributes are dropped. More.</p>", "Attributes are dropped.")]
    [InlineData(null, "<P>Upper case tags. More.</P>", "Upper case tags.")]
    [InlineData(null, "<p>Version 1.2 is out. Update.</p>", "Version 1.2 is out.")]
    [InlineData(null, "<p>Line one\nline two. Three.</p>", "Line one line two.")]
    [InlineData(null, "<h2>Only a heading</h2>", "Only a heading")]
    [InlineData(null, "<ul><li>First item. Second.</li></ul>", "First item.")]
    [InlineData(null, "<p><script>alert(1)</script>Safe text. More.</p>", "Safe text.")]
    [InlineData(null, "<p><style>p{}</style>Styled. More.</p>", "Styled.")]
    [InlineData(null, "<p><img src=\"x.png\" alt=\"pic\"> After the image. More.</p>", "After the image.")]
    public void Without_a_summary_it_is_the_first_sentence_of_the_first_paragraph_as_plain_text(string? summary, string html, string expected) => KbPlainText.Describe(summary, html).ShouldBe(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "<p></p>")]
    [InlineData(null, "<img src=\"x.png\">")]
    [InlineData(null, "<table><tr><td> </td></tr></table>")]
    public void No_words_at_all_is_an_empty_description(string? summary, string? html) => KbPlainText.Describe(summary, html).ShouldBeEmpty();

    [Fact]
    public void A_long_sentence_is_cut_at_a_word_with_an_ellipsis_within_the_limit()
    {
        var sentence = string.Join(' ', Enumerable.Repeat("wordy", 60)) + ".";

        var description = KbPlainText.Describe(null, "<p>" + sentence + "</p>");

        description.Length.ShouldBeLessThanOrEqualTo(KbPlainText.MaxDescription + 3);
        description.ShouldEndWith("wordy...");
        KbPlainText.Describe(new string('a', 400), null).ShouldBe(new string('a', KbPlainText.MaxDescription) + "...", "no space to cut at: cut at the limit");
        KbPlainText.MaxDescription.ShouldBe(160);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var text = new string('a', KbPlainText.MaxDescription - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        var description = KbPlainText.Describe(text, null);

        description.ShouldBe(new string('a', KbPlainText.MaxDescription - 1) + "...");
    }

    [Fact]
    public void Markup_in_the_body_never_survives_into_the_description()
    {
        var description = KbPlainText.Describe(null, "<p>&lt;script&gt;alert(1)&lt;/script&gt; and <a href=\"javascript:alert(1)\">link</a>. Next.</p>");

        description.ShouldBe("<script>alert(1)</script> and link.");
    }
}
