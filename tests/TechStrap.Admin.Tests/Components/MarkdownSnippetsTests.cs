using TechStrap.Admin.Features.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The toolbar adds Markdown at the end of the text, because Blazor cannot read the caret. These are the rules of where and how.</summary>
public sealed class MarkdownSnippetsTests
{
    [Fact]
    public void A_snippet_in_an_empty_article_is_the_whole_text()
    {
        MarkdownSnippets.Append(null, MarkdownSnippets.Bold, inline: true).ShouldBe("**bold text**");
        MarkdownSnippets.Append("", MarkdownSnippets.List, inline: false).ShouldBe("- first item\n- second item");
    }

    [Theory]
    [InlineData("Hello", "Hello **bold text**")]
    [InlineData("Hello ", "Hello **bold text**")]
    [InlineData("Hello\n", "Hello\n**bold text**")]
    public void An_inline_snippet_follows_a_space_and_never_glues_itself_to_the_last_word(string text, string expected) =>
        MarkdownSnippets.Append(text, MarkdownSnippets.Bold, inline: true).ShouldBe(expected);

    [Theory]
    [InlineData("Hello", "Hello\n\n```\ncode\n```")]
    [InlineData("Hello\n", "Hello\n\n```\ncode\n```")]
    [InlineData("Hello\n\n", "Hello\n\n```\ncode\n```")]
    public void A_block_snippet_starts_after_a_blank_line(string text, string expected) =>
        MarkdownSnippets.Append(text, MarkdownSnippets.Code, inline: false).ShouldBe(expected);

    [Fact]
    public void The_toolbar_snippets_are_the_markdown_the_preview_renders()
    {
        MarkdownSnippets.Italic.ShouldBe("*italic text*");
        MarkdownSnippets.Link.ShouldBe("[link text](https://)");
    }

    [Fact]
    public void An_image_is_markdown_with_its_alt_text_and_an_address_that_cannot_end_the_link_early()
    {
        MarkdownSnippets.Image("Login screen", "https://api.example/kb-images/abc.png").ShouldBe("![Login screen](https://api.example/kb-images/abc.png)");
        MarkdownSnippets.Image("x", " https://api.example/a b(1).png ").ShouldBe("![x](https://api.example/a%20b%281%29.png)");
    }

    [Theory]
    [InlineData("login-screen.png", "login screen")]
    [InlineData("My_Shot.JPEG", "My Shot")]
    [InlineData("a[b](c)`d`*e*.png", "a b c d e")]
    [InlineData(".png", "image")]
    [InlineData("", "image")]
    [InlineData(null, "image")]
    public void The_alt_text_comes_from_the_file_name_without_markdown_characters(string? fileName, string expected) =>
        MarkdownSnippets.AltFromFileName(fileName).ShouldBe(expected);

    [Fact]
    public void A_very_long_file_name_is_cut_for_the_alt_text()
    {
        var alt = MarkdownSnippets.AltFromFileName(new string('a', 300) + ".png");

        alt.Length.ShouldBe(MarkdownSnippets.MaxAltLength);
    }

    [Fact]
    public void Alt_text_can_never_close_the_bracket_or_start_html()
    {
        var image = MarkdownSnippets.Image("a](https://evil.example) <img src=x onerror=alert(1)>", "https://api.example/x.png");

        image.ShouldBe("![a https://evil.example img src=x onerror=alert 1](https://api.example/x.png)");
        image.Count(c => c == ']').ShouldBe(1);
        image.ShouldNotContain("<");
    }

    [Fact]
    public void An_address_never_carries_angle_brackets_quotes_or_control_characters()
    {
        MarkdownSnippets.Image("x", "https://api.example/a<b>\"c\u0007d\u007f.png").ShouldBe("![x](https://api.example/a%3Cb%3E%22c%07d%7F.png)");
    }
}
