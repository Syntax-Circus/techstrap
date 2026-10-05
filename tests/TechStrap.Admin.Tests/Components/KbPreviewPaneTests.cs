using Bunit;
using TechStrap.Admin.Features.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The preview pane is the one place the Admin draws HTML from an agent's text (Review Focus 1). It draws the sanitised HTML the API returned and nothing else: not the text the agent typed, not an error
/// message from the API, and nothing of its own besides fixed copy.
/// </summary>
public sealed class KbPreviewPaneTests : BunitContext
{
    [Fact]
    public void It_draws_the_html_it_was_given_exactly_as_the_api_returned_it()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.Html, "<h2>Steps</h2><p>Open <strong>Settings</strong>.</p><img src=\"https://api.example/kb-images/a.png\" alt=\"Shot\">"));

        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<h2>Steps</h2><p>Open <strong>Settings</strong>.</p><img src=\"https://api.example/kb-images/a.png\" alt=\"Shot\">");
        cut.FindAll(".ts-kb-preview-empty").ShouldBeEmpty();
        cut.Find("section").GetAttribute("aria-label").ShouldBe("Preview of the article");
    }

    [Fact]
    public void Before_the_first_answer_it_says_there_is_nothing_to_preview_and_while_a_call_runs_it_says_it_is_updating()
    {
        var empty = Render<KbPreviewPane>();
        empty.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Nothing to preview yet.");
        empty.Find("section").HasAttribute("aria-busy").ShouldBeFalse();

        var busy = Render<KbPreviewPane>(p => p.Add(c => c.Loading, true));
        busy.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Updating the preview");
        busy.Find("section").GetAttribute("aria-busy").ShouldBe("true");
    }

    [Fact]
    public void A_failure_keeps_the_last_good_preview_under_a_fixed_message()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.Html, "<p>Earlier</p>").Add(c => c.ErrorText, KbEditorCopy.PreviewFailed));

        cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("The preview is not available right now.");
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>Earlier</p>");
    }

    [Fact]
    public void A_failure_with_nothing_to_show_has_the_message_and_no_empty_line()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.ErrorText, KbEditorCopy.PreviewFailed));

        cut.Find(".ts-kb-preview-error").ShouldNotBeNull();
        cut.FindAll(".ts-kb-preview-empty").ShouldBeEmpty();
        cut.FindAll(".ts-kb-preview-body").ShouldBeEmpty();
    }
}
