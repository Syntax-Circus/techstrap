using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Tags;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 5 for tags: a tag in use is never deleted without its name typed, and only then with force; an unused tag asks first and is never forced; nothing fires twice. The dialog shows the
/// ticket count from the list, and a tag that was tagged after the list was read is caught by the API's 409 and shown again with its new count.
/// </summary>
public sealed class DeleteTagTests : AdminPageTest
{
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();
    private readonly Guid _urgentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010");
    private readonly Guid _unusedId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012");

    public DeleteTagTests()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<TagSummaryDto>>(
        [
            TestData.TagSummary("urgent", 12, id: _urgentId),
            TestData.TagSummary("Old idea", 0, "old-idea", id: _unusedId),
        ]));
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_tags);
    }

    private IRenderedComponent<TagsPage> RenderPage() => Render<TagsPage>();

    private IReadOnlyList<(Guid Id, bool Force)> Deletes() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.DeleteAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (bool)c.GetArguments()[1]!))];

    private static void AskToDelete(IRenderedComponent<TagsPage> cut, string slug) => cut.Find($"tr[data-tag='{slug}'] button.ts-delete").Click();

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<TagsPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Delete the tag ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<TagsPage> cut) => Dialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    private static void Type(IRenderedComponent<TagsPage> cut, string text) => Dialog(cut).QuerySelector("input")!.Input(text);

    // ---- a tag in use: typed name, count, force ------------------------------------------------------------------

    [Fact]
    public void A_tag_in_use_shows_its_ticket_count_asks_for_the_name_typed_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        AskToDelete(cut, "urgent");

        var dialog = Dialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Delete the tag urgent?");
        dialog.QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("12 tickets");
        dialog.TextContent.ShouldContain("This tag is on 12 tickets. Deleting it removes it from all of them, and each ticket records the change. This can't be undone.");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type urgent to confirm");
        dialog.ClassName!.ShouldContain("ts-dialog--danger");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Dialogs.VerifyInvoke("open", 1);
        Deletes().ShouldBeEmpty();
    }

    [Fact]
    public void One_ticket_is_said_in_the_singular()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 1, id: _urgentId)]));
        var cut = RenderPage();

        AskToDelete(cut, "urgent");

        Dialog(cut).QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("1 ticket");
    }

    [Fact]
    public void Without_the_name_typed_confirm_does_nothing_and_a_near_miss_is_not_enough()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");

        Confirm(cut).Click();
        Type(cut, "urgen");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Type(cut, "urgent now");
        Confirm(cut).Click();

        Deletes().ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void Typing_the_name_in_any_case_deletes_once_with_force_removes_the_row_and_says_how_many_tickets_lost_it()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "URGENT");

        Confirm(cut).Click();

        Deletes().ShouldBe([(_urgentId, true)]);
        _tags.Received(1).DeleteAsync(_urgentId, true, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.FindAll("tr[data-tag='urgent']").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Deleted the tag urgent and removed it from 12 tickets");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_double_click_on_confirm_deletes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");

        Confirm(cut).Click();
        Dialog(cut).QuerySelectorAll("button, input").ShouldAllBe(e => e.HasAttribute("disabled"));
        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Deletes().Count.ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll("tr[data-tag='urgent']").ShouldBeEmpty());
    }

    [Fact]
    public void Cancel_and_Esc_close_the_dialog_and_make_no_call_and_reopening_starts_with_an_empty_box()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");

        Dialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToDelete(cut, "urgent");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue("the typed name from the first time is gone");
        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Deletes().ShouldBeEmpty();
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr[data-tag='urgent']").Count.ShouldBe(1);
    }

    // ---- an unused tag: a medium confirmation, never forced ------------------------------------------------------

    [Fact]
    public void An_unused_tag_asks_first_with_no_typed_name_and_deletes_once_without_force()
    {
        var cut = RenderPage();

        AskToDelete(cut, "old-idea");

        var dialog = Dialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Delete the tag Old idea?");
        dialog.TextContent.ShouldContain("No tickets use this tag. This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull();
        dialog.ClassName!.ShouldNotContain("ts-dialog--danger");
        Deletes().ShouldBeEmpty();

        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false)]);
        StatusMessages.Current.ShouldBe("Deleted the tag Old idea");
        cut.FindAll("tr[data-tag='old-idea']").ShouldBeEmpty();
    }

    [Fact]
    public void An_unused_tag_is_never_deleted_by_cancel_or_escape()
    {
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        AskToDelete(cut, "old-idea");
        Dialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();

        Deletes().ShouldBeEmpty();
    }

    // ---- when the list was stale ---------------------------------------------------------------------------------

    [Fact]
    public void A_tag_that_gained_tickets_since_the_list_was_read_is_refused_by_the_api_shown_again_with_the_new_count_and_needs_the_name()
    {
        _tags.DeleteAsync(_unusedId, false, Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.TagInUse, "This tag is on 3 ticket(s). Delete it with force to remove it from them first.", ResultErrorKind.Conflict));
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 12, id: _urgentId), TestData.TagSummary("Old idea", 0, "old-idea", id: _unusedId)]),
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 12, id: _urgentId), TestData.TagSummary("Old idea", 3, "old-idea", id: _unusedId)]));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false)]);
        var dialog = Dialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("This tag was just added to tickets. The count above is updated: type the name to delete it anyway.");
        dialog.QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("3 tickets");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type Old idea to confirm");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);

        Type(cut, "old idea");
        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false), (_unusedId, true)]);
    }

    [Fact]
    public void A_tag_that_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.TagNotFound, "No such tag.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That tag no longer exists.");
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void Another_failure_keeps_the_dialog_open_and_says_nothing_was_changed()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        Dialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't delete the tag. Nothing was changed. The API refused.");
        cut.FindAll("tr[data-tag='old-idea']").Count.ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_delete_and_offers_a_reload()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");
        Confirm(cut).Click();

        var dialog = Dialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The delete may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);

        Dialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_delete_that_finishes_after_the_page_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");
        Confirm(cut).Click();

        cut.FindComponent<TagsContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _tags.Received(1).DeleteAsync(_unusedId, false, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
    }
}
