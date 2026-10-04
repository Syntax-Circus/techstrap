using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ConfirmDialogTests : AdminComponentTest
{
    private int _confirmed;
    private int _cancelled;

    private IRenderedComponent<ConfirmDialog> RenderDialog(Action<ComponentParameterCollectionBuilder<ConfirmDialog>>? more = null) =>
        Render<ConfirmDialog>(p =>
        {
            p.Add(c => c.Open, true)
                .Add(c => c.Title, "Delete ACME-142?")
                .Add(c => c.ConfirmLabel, "Delete ticket")
                .Add(c => c.OnConfirm, () => _confirmed++)
                .Add(c => c.OnCancel, () => _cancelled++)
                .AddChildContent("<p>This cannot be undone.</p>");
            more?.Invoke(p);
        });

    private static void Type(IRenderedComponent<ConfirmDialog> cut, string text) => cut.Find("input").Input(text);

    [Fact]
    public void Opening_shows_the_modal_once_and_focuses_the_heading_when_nothing_must_be_typed()
    {
        var cut = RenderDialog();

        Dialogs.VerifyInvoke("open", 1);
        var open = Dialogs.Invocations["open"].Single();
        open.Arguments[1].ShouldBeElementReferenceTo(cut.Find("h2"));
        cut.Find("h2").GetAttribute("tabindex").ShouldBe("-1");
    }

    [Fact]
    public void A_typed_confirmation_takes_the_initial_focus()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        Dialogs.Invocations["open"].Single().Arguments[1].ShouldBeElementReferenceTo(cut.Find("input"));
        cut.Find("label").TextContent.ShouldBe("Type ACME-142 to confirm");
    }

    [Fact]
    public void A_closed_dialog_never_calls_the_script()
    {
        Render<ConfirmDialog>(p => p.Add(c => c.Open, false).Add(c => c.Title, "x"));

        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void Closing_asks_the_browser_to_close_the_dialog()
    {
        var cut = RenderDialog();

        cut.Render(p => p.Add(c => c.Open, false));

        Dialogs.VerifyInvoke("close", 1);
    }

    [Theory]
    [InlineData("ACME-142", true)]
    [InlineData("  acme-142 ", true)]
    [InlineData("ACME-14", false)]
    [InlineData("", false)]
    public void Confirm_is_enabled_only_when_the_typed_text_matches(string typed, bool enabled)
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        Type(cut, typed);

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBe(!enabled);
    }

    [Fact]
    public void Confirm_fires_once_and_a_disabled_confirm_fires_nothing()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        cut.Find("button.btn-primary").Click();
        _confirmed.ShouldBe(0);

        Type(cut, "ACME-142");
        cut.Find("button.btn-primary").Click();

        _confirmed.ShouldBe(1);
    }

    [Fact]
    public void Cancel_fires_once_and_never_confirms()
    {
        var cut = RenderDialog();

        cut.Find("button.btn-outline-secondary").Click();

        _cancelled.ShouldBe(1);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void Enter_cannot_confirm_because_there_is_no_form_and_the_buttons_are_not_submit_buttons()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));
        Type(cut, "ACME-142");

        cut.FindAll("form").ShouldBeEmpty();
        cut.FindAll("button").ShouldAllBe(button => button.GetAttribute("type") == "button");
        cut.Find("input").HasAttribute("onkeydown").ShouldBeFalse();
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void Escape_the_native_cancel_event_cancels_the_dialog()
    {
        var cut = RenderDialog();

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _cancelled.ShouldBe(1);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void While_busy_everything_is_inert_including_escape()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142").Add(c => c.Busy, true));

        cut.FindAll("button").ShouldAllBe(button => button.HasAttribute("disabled"));
        cut.Find("input").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _cancelled.ShouldBe(0);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void A_failure_is_shown_as_an_alert_and_the_dialog_stays_open()
    {
        var cut = RenderDialog(p => p.Add(c => c.Error, "Couldn't delete the ticket. Nothing was changed."));

        cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't delete the ticket. Nothing was changed.");
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void An_informational_dialog_has_only_a_close_button()
    {
        var cut = RenderDialog(p => p.Add(c => c.Informational, true).Add(c => c.CancelLabel, "Close"));

        cut.FindAll("button").Count.ShouldBe(1);
        cut.Find("button").TextContent.ShouldBe("Close");
    }

    [Fact]
    public void Reopening_clears_what_was_typed_before()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));
        Type(cut, "ACME-142");
        cut.Render(p => p.Add(c => c.Open, false));

        cut.Render(p => p.Add(c => c.Open, true));

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_dangerous_dialog_uses_the_danger_style_on_the_confirm_button()
    {
        var cut = RenderDialog(p => p.Add(c => c.Danger, true));

        cut.Find("dialog").ClassList.ShouldContain("ts-dialog--danger");
        cut.Find("button.btn-danger").TextContent.ShouldBe("Delete ticket");
    }
}
