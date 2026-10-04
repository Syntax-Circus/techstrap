using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 2: the API key plaintext leaks. It is shown once, in the dialog's own field, with a copy button; the dialog ignores Cancel and Esc until "I have stored this key" is ticked; and once it
/// closes the key is gone from the component and from the markup. It is never logged, never in a URL, and appears in the markup exactly once while the dialog is open.
/// </summary>
public sealed class NewApiKeyDialogTests : AdminComponentTest
{
    private const string Secret = "tsk_live_9f8e7d6c5b4a39281706f5e4d3c2b1a0";

    private readonly RecordingLoggerProvider _logs = new();
    private readonly NavigationManager _navigation;
    private BunitJSModuleInterop? _clipboard;
    private int _closed;

    public NewApiKeyDialogTests()
    {
        Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<NewApiKeyDialog> RenderDialog() =>
        Render<NewApiKeyDialog>(p => p.Add(c => c.ProductName, "Orbitly").Add(c => c.OnClosed, () => _closed++));

    private static async Task ShowAsync(IRenderedComponent<NewApiKeyDialog> cut, string kind = ApiKeyKinds.Trusted, string secret = Secret) =>
        await cut.InvokeAsync(() => cut.Instance.Show(TestData.ApiKey(kind: kind), secret));

    private void SetupClipboard(bool copied)
    {
        _clipboard = JSInterop.SetupModule("./js/clipboard.js");
        _clipboard.Setup<bool>("copyText", _ => true).SetResult(copied);
        _clipboard.SetupVoid("selectText", _ => true).SetVoidResult();
    }

    private static int Occurrences(string markup, string text) => Regex.Matches(markup, Regex.Escape(text)).Count;

    private static AngleSharp.Dom.IElement Done(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find(".ts-dialog-actions button:not(.btn-outline-secondary)");

    private static AngleSharp.Dom.IElement Close(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find(".ts-dialog-actions button.btn-outline-secondary");

    private static void TickStored(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find("#ts-key-stored").Change(true);

    // ---- what it shows --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_closed_dialog_has_no_key_in_the_markup_and_never_opens_the_script()
    {
        var cut = RenderDialog();

        cut.Markup.ShouldNotContain(Secret);
        cut.FindAll("#ts-key-secret").ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("open");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Showing_a_key_opens_the_dialog_with_the_key_in_a_read_only_field_the_kind_and_the_stored_box_unticked()
    {
        var cut = RenderDialog();

        await ShowAsync(cut, ApiKeyKinds.Public);

        Dialogs.VerifyInvoke("open", 1);
        cut.Find("h2").TextContent.ShouldBe("Your new API key");
        var field = cut.Find("input#ts-key-secret");
        field.GetAttribute("value").ShouldBe(Secret);
        field.HasAttribute("readonly").ShouldBeTrue();
        field.GetAttribute("aria-label").ShouldBe("New API key");
        cut.Find(".ts-kind").TextContent.ShouldBe("Public");
        cut.Find(".ts-key-kind-line").TextContent.ShouldContain("Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted");
        cut.Markup.ShouldContain("This is the only time the key for Orbitly is shown.");
        cut.Find("label[for=ts-key-stored]").TextContent.ShouldBe("I have stored this key");
        cut.Find("#ts-key-stored").HasAttribute("checked").ShouldBeFalse();
        Done(cut).HasAttribute("disabled").ShouldBeTrue();
        Occurrences(cut.Markup, Secret).ShouldBe(1, "the key appears once, in the read-only field, and nowhere else");
    }

    [Fact]
    public async Task The_key_is_in_no_url_no_link_and_no_other_attribute()
    {
        var cut = RenderDialog();

        await ShowAsync(cut);

        _navigation.Uri.ShouldNotContain(Secret);
        cut.FindAll("[href]").ShouldAllBe(e => !e.GetAttribute("href")!.Contains(Secret));
        cut.FindAll("*").SelectMany(e => e.Attributes).Where(a => a.Value.Contains(Secret)).Select(a => $"{a.Name}").ShouldBe(["value"]);
    }

    // ---- it cannot be dismissed before "stored" ------------------------------------------------------------------

    [Fact]
    public async Task Esc_and_Cancel_do_nothing_but_say_what_is_missing_until_the_stored_box_is_ticked()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);
        Close(cut).Click();
        Done(cut).Click();

        _closed.ShouldBe(0);
        Dialogs.VerifyNotInvoke("close");
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
        cut.Find(".ts-dialog-error[role=alert]").TextContent.ShouldBe("Tick \"I have stored this key\" before you close this window.");
    }

    [Fact]
    public async Task Ticking_the_box_clears_the_hint_and_enables_Done()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        Close(cut).Click();
        cut.FindAll(".ts-dialog-error").Count.ShouldBe(1);

        TickStored(cut);

        cut.FindAll(".ts-dialog-error").ShouldBeEmpty();
        Done(cut).HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public async Task Done_after_the_tick_closes_once_clears_the_key_and_leaves_no_trace_in_the_markup()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);

        Done(cut).Click();

        _closed.ShouldBe(1);
        Dialogs.VerifyInvoke("close", 1);
        cut.Markup.ShouldNotContain(Secret);
        cut.FindAll("#ts-key-secret").ShouldBeEmpty();
        cut.FindAll("#ts-key-stored").ShouldBeEmpty();
        _navigation.Uri.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Esc_and_Close_work_once_the_box_is_ticked_and_clear_the_key_too()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _closed.ShouldBe(1);
        cut.Markup.ShouldNotContain(Secret);

        await ShowAsync(cut, secret: "tsk_second");
        TickStored(cut);
        Close(cut).Click();
        _closed.ShouldBe(2);
        cut.Markup.ShouldNotContain("tsk_second");
    }

    [Fact]
    public async Task A_second_key_starts_clean_with_the_box_unticked_and_the_first_key_gone()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);
        Done(cut).Click();

        await ShowAsync(cut, secret: "tsk_second");

        cut.Markup.ShouldNotContain(Secret);
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe("tsk_second");
        cut.Find("#ts-key-stored").HasAttribute("checked").ShouldBeFalse();
        Done(cut).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_the_dialog_drops_the_key()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);

        await cut.Instance.DisposeAsync();

        typeof(NewApiKeyDialog).GetField("_plaintext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance).ShouldBeNull();
    }

    // ---- copy ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Copy_sends_the_key_to_the_clipboard_script_once_and_confirms_without_selecting()
    {
        SetupClipboard(copied: true);
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status[role=status]").TextContent.ShouldBe("Copied to the clipboard."));
        var clipboard = _clipboard!;
        clipboard.VerifyInvoke("copyText", 1);
        clipboard.Invocations["copyText"].Single().Arguments.ShouldBe([Secret]);
        clipboard.VerifyNotInvoke("selectText");
    }

    [Fact]
    public async Task When_the_browser_refuses_the_copy_the_text_is_selected_and_the_agent_is_told_to_press_Ctrl_C()
    {
        SetupClipboard(copied: false);
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldBe("Couldn't copy. The key is selected: press Ctrl+C to copy it."));
        var clipboard = _clipboard!;
        clipboard.VerifyInvoke("selectText", 1);
        // What is selected is the element reference of the key field: the script gets the element itself, never the text a second time.
        var argument = clipboard.Invocations["selectText"].Single().Arguments.ShouldHaveSingleItem();
        argument.ShouldBeOfType<ElementReference>().Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_script_that_cannot_run_is_the_same_as_a_refused_copy_and_never_breaks_the_dialog()
    {
        _clipboard = JSInterop.SetupModule("./js/clipboard.js");
        _clipboard.Setup<bool>("copyText", _ => true).SetException(new JSException("clipboard blocked"));
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldBe("Couldn't copy. The key is selected: press Ctrl+C to copy it."));
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
    }

    // ---- logs ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_key_is_in_no_log_line_through_a_whole_show_copy_and_close()
    {
        Services.GetRequiredService<ILoggerFactory>().CreateLogger("probe").LogInformation("the sink is wired");
        SetupClipboard(copied: true);
        var cut = RenderDialog();
        await ShowAsync(cut);
        cut.Find(".ts-key-secret button").Click();
        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldNotBeEmpty());
        TickStored(cut);
        Done(cut).Click();

        _logs.Lines.ShouldContain(line => line.Contains("the sink is wired"), "the recorder must be wired, or this test proves nothing");
        _logs.Lines.ShouldAllBe(line => !line.Contains(Secret));
    }
}
