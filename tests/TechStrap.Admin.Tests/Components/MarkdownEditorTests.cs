using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The Markdown editor: a textarea, a toolbar and a live preview that asks the API. A burst of typing is one call 300 ms after the last keystroke; a call that a newer change replaced is cancelled and its answer
/// is never drawn; a failed preview keeps the text; nothing is previewed for an empty text; and the timer and the call go when the component does (PHASE-08 T16).
/// </summary>
public sealed class MarkdownEditorTests : AdminComponentTest
{
    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly CountingTimeProvider _timers;

    public MarkdownEditorTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call => TestData.Ok(new KbPreviewResponse($"<p>{call.Arg<KbPreviewRequest>().BodyMarkdown}</p>")));
        Services.AddSingleton(_kb);
    }

    // Stands in for the page: it owns the text and passes it down, as the editor page does.
    private sealed class Host : ComponentBase
    {
        [Parameter]
        public string Text { get; set; } = string.Empty;

        [Parameter]
        public bool Disabled { get; set; }

        [Parameter]
        public string? Error { get; set; }

        private void SetTooComplex(bool tooComplex) => Error = tooComplex ? KbEditorCopy.BodyTooComplex : null;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MarkdownEditor>(0);
            builder.AddAttribute(1, nameof(MarkdownEditor.Id), "ts-kb-body");
            builder.AddAttribute(2, nameof(MarkdownEditor.Value), Text);
            builder.AddAttribute(3, nameof(MarkdownEditor.ValueChanged), EventCallback.Factory.Create<string>(this, value => Text = value));
            builder.AddAttribute(4, nameof(MarkdownEditor.Disabled), Disabled);
            builder.AddAttribute(5, nameof(MarkdownEditor.ErrorMessage), Error);
            builder.AddAttribute(6, nameof(MarkdownEditor.OnTooComplex), EventCallback.Factory.Create<bool>(this, SetTooComplex));
            builder.CloseComponent();
        }
    }

    private IRenderedComponent<Host> RenderEditor(string text = "") => Render<Host>(p => p.Add(h => h.Text, text));

    private IEnumerable<string?> PreviewedTexts() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.PreviewAsync)).Select(c => ((KbPreviewRequest)c.GetArguments()[0]!).BodyMarkdown);

    private static string TextArea(IRenderedComponent<Host> cut) => cut.Find("textarea").GetAttribute("value")!;

    // ---- the preview call ----------------------------------------------------------------------------------------

    [Fact]
    public void A_burst_of_typing_is_one_preview_call_300_ms_after_the_last_keystroke()
    {
        var cut = RenderEditor();

        cut.Find("textarea").Input("a");
        cut.Find("textarea").Input("ab");
        cut.Find("textarea").Input("abc");
        Time.Advance(KbDefaults.PreviewDebounce - TimeSpan.FromMilliseconds(1));
        PreviewedTexts().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["abc"]));
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>abc</p>"));
        Time.Advance(KbDefaults.PreviewDebounce * 5);
        PreviewedTexts().ShouldBe(["abc"]);
        KbDefaults.PreviewDebounce.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void An_article_that_is_already_written_is_previewed_once_when_the_editor_opens()
    {
        var cut = RenderEditor("# Steps");

        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["# Steps"]));
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps</p>"));
    }

    [Fact]
    public void A_change_from_outside_such_as_a_reload_is_previewed_too()
    {
        var cut = RenderEditor("one");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["one"]));

        cut.Render(p => p.Add(h => h.Text, "two"));
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["one", "two"]));
    }

    [Fact]
    public void Nothing_is_previewed_for_an_empty_text_and_clearing_the_text_clears_the_preview_without_a_call()
    {
        var cut = RenderEditor();
        Time.Advance(KbDefaults.PreviewDebounce * 2);
        PreviewedTexts().ShouldBeEmpty();
        cut.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Nothing to preview yet.");

        cut.Find("textarea").Input("hello");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>hello</p>"));

        cut.Find("textarea").Input("   ");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-preview-body").ShouldBeEmpty());
        PreviewedTexts().ShouldBe(["hello"]);
    }

    [Fact]
    public async Task A_call_that_a_newer_change_replaced_is_cancelled_and_its_late_answer_is_never_drawn()
    {
        var first = new TaskCompletionSource<Result<KbPreviewResponse>>();
        CancellationToken firstToken = default;
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "old"), Arg.Any<CancellationToken>()).Returns(call =>
        {
            firstToken = call.Arg<CancellationToken>();
            return first.Task;
        });
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "new"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>NEW</p>")));
        var cut = RenderEditor();
        cut.Find("textarea").Input("old");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["old"]));
        firstToken.IsCancellationRequested.ShouldBeFalse();

        cut.Find("textarea").Input("new");

        firstToken.IsCancellationRequested.ShouldBeTrue();
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>NEW</p>"));
        first.SetResult(TestData.Ok(new KbPreviewResponse("<p>OLD</p>")));

        // The late answer is handled on the renderer's thread: let it run, then draw again, so a guard that is missing would show.
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(h => h.Error, "refresh"));
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>NEW</p>");
        cut.Markup.ShouldNotContain("<p>OLD</p>");
    }

    [Fact]
    public async Task An_answer_that_arrives_after_a_newer_call_started_is_ignored_even_when_the_call_was_not_cancelled()
    {
        var slow = new TaskCompletionSource<Result<KbPreviewResponse>>();
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "slow"), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "fast"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>fast</p>")));
        var cut = RenderEditor();
        cut.Find("textarea").Input("slow");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["slow"]));
        cut.Find("textarea").Input("fast");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fast</p>"));

        slow.SetResult(TestData.Ok(new KbPreviewResponse("<p>slow</p>")));
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(h => h.Error, "refresh"));

        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fast</p>");
        cut.Markup.ShouldNotContain("<p>slow</p>");
    }

    [Fact]
    public void A_failed_preview_keeps_the_text_and_the_last_good_preview_and_says_so_in_fixed_words()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("good");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>"));
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbPreviewResponse>("api-error", "<img src=x onerror=alert(1)>"));

        cut.Find("textarea").Input("good and more");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("The preview is not available right now."));
        TextArea(cut).ShouldBe("good and more");
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>");
        cut.Markup.ShouldNotContain("onerror");

        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>fine</p>")));
        cut.Find("textarea").Input("good and more!");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-preview-error").ShouldBeEmpty());
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fine</p>");
    }

    [Fact]
    public void A_body_the_api_calls_too_complex_is_a_calm_fixed_message_and_keeps_the_last_good_preview()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("good");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>"));
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(
            Result<KbPreviewResponse>.Failure(new ResultError(ApiErrorCodes.KbBodyTooComplex, "<b>api words</b>", ResultErrorKind.Validation, ApiFields.Body)));

        cut.Find("textarea").Input("good and a huge table");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").TextContent.ShouldBe(KbEditorCopy.PreviewTooComplex));
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>");
        cut.Markup.ShouldNotContain("api words");
        cut.Find("#ts-kb-body-error").TextContent.ShouldBe(KbEditorCopy.BodyTooComplex);
        TextArea(cut).ShouldBe("good and a huge table");
    }

    [Fact]
    public void A_preview_that_throws_is_a_failed_preview_and_never_reaches_the_renderer()
    {
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns<Task<Result<KbPreviewResponse>>>(_ => throw new InvalidOperationException("secret text from the article"));
        var cut = RenderEditor();

        cut.Find("textarea").Input("boom");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").ShouldNotBeNull());
        cut.Markup.ShouldNotContain("secret text");
        TextArea(cut).ShouldBe("boom");
    }

    [Fact]
    public void A_text_the_api_could_not_save_either_is_not_sent_for_preview_and_keeps_the_last_good_preview()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("short");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>short</p>"));

        cut.Find("textarea").Input(new string('a', KbEditorLimits.BodyMaxLength + 1));
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("too long to preview or save"));
        PreviewedTexts().ShouldBe(["short"]);
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>short</p>");
    }

    [Fact]
    public void While_a_call_runs_the_pane_is_marked_busy()
    {
        var pending = new TaskCompletionSource<Result<KbPreviewResponse>>();
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderEditor();

        cut.Find("textarea").Input("x");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find("section.ts-kb-preview").GetAttribute("aria-busy").ShouldBe("true"));
        pending.SetResult(TestData.Ok(new KbPreviewResponse("<p>x</p>")));
        cut.WaitForAssertion(() => cut.Find("section.ts-kb-preview").HasAttribute("aria-busy").ShouldBeFalse());
    }

    // ---- teardown ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("typed");

        _timers.LiveTimers.ShouldBe(1);

        cut.FindComponent<MarkdownEditor>().Instance.Dispose();

        // Released at once, not when it would have fired.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(KbDefaults.PreviewDebounce * 3);
        PreviewedTexts().ShouldBeEmpty();
    }

    [Fact]
    public void A_call_in_flight_is_cancelled_with_the_component()
    {
        var pending = new TaskCompletionSource<Result<KbPreviewResponse>>();
        CancellationToken token = default;
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var cut = RenderEditor();
        cut.Find("textarea").Input("typed");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["typed"]));

        cut.FindComponent<MarkdownEditor>().Instance.Dispose();

        token.IsCancellationRequested.ShouldBeTrue();
    }

    // ---- the toolbar ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Bold", "**bold text**")]
    [InlineData("Italic", "*italic text*")]
    [InlineData("Link", "[link text](https://)")]
    [InlineData("List", "- first item\n- second item")]
    [InlineData("Code", "```\ncode\n```")]
    public void Each_toolbar_button_adds_its_markdown_at_the_end_of_the_text_and_the_preview_follows(string button, string snippet)
    {
        var cut = RenderEditor("Intro");

        cut.FindAll(".ts-kb-toolbar button").Single(b => b.TextContent.Trim().EndsWith(button, StringComparison.Ordinal)).Click();

        var inline = button is "Bold" or "Italic" or "Link";
        TextArea(cut).ShouldBe(inline ? $"Intro {snippet}" : $"Intro\n\n{snippet}");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().Last().ShouldBe(inline ? $"Intro {snippet}" : $"Intro\n\n{snippet}"));
    }

    [Fact]
    public void The_toolbar_is_a_named_toolbar_that_controls_the_text_area()
    {
        var cut = RenderEditor();

        var toolbar = cut.Find("[role=toolbar]");
        toolbar.GetAttribute("aria-label").ShouldBe("Formatting");
        toolbar.GetAttribute("aria-controls").ShouldBe("ts-kb-body");
        cut.Find("textarea").Id.ShouldBe("ts-kb-body");
        cut.Find("label[for=ts-kb-body]").TextContent.ShouldBe("Article");
    }

    [Fact]
    public void A_picture_that_was_uploaded_is_added_as_markdown_with_its_alt_text_from_the_file_name()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        var cut = RenderEditor("Intro");

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[4], "login-screen.png", null, "image/png"));

        cut.WaitForAssertion(() => TextArea(cut).ShouldBe("Intro\n\n![login screen](https://api.example/kb-images/a.png)"));
    }

    [Fact]
    public void A_busy_form_disables_the_text_and_every_toolbar_button()
    {
        var cut = Render<Host>(p => p.Add(h => h.Text, "x").Add(h => h.Disabled, true));

        cut.Find("textarea").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll(".ts-kb-toolbar button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_field_error_is_tied_to_the_text_area()
    {
        var cut = Render<Host>(p => p.Add(h => h.Text, "x").Add(h => h.Error, "Write the article before you save it."));

        cut.Find("textarea").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find("textarea").GetAttribute("aria-describedby").ShouldBe("ts-kb-body-error");
        cut.Find("#ts-kb-body-error").TextContent.ShouldBe("Write the article before you save it.");
        cut.Find("textarea").ClassList.ShouldContain("is-invalid");
    }

    [Fact]
    public void Below_992px_the_write_and_preview_buttons_choose_which_pane_shows()
    {
        var cut = RenderEditor("x");

        cut.Find(".ts-kb-md").GetAttribute("data-pane").ShouldBe("write");
        var tabs = cut.FindAll(".ts-kb-tabs button");
        tabs[0].GetAttribute("aria-pressed").ShouldBe("true");
        tabs[1].GetAttribute("aria-pressed").ShouldBe("false");

        tabs[1].Click();

        cut.Find(".ts-kb-md").GetAttribute("data-pane").ShouldBe("preview");
        cut.FindAll(".ts-kb-tabs button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find("textarea").GetAttribute("value").ShouldBe("x");
    }
}
