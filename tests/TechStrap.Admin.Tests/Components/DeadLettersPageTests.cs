using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Ops.DeadLetters;
using TechStrap.Admin.Features.Settings;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The failed emails. Review Focus 5: Discard asks first and fires once; Retry needs no confirmation and fires once. Every outcome that is not a plain success refreshes or explains, an unknown outcome never claims
/// nothing happened, and the navigation badge is refreshed after every write.
/// </summary>
public sealed class DeadLettersPageTests : AdminPageTest
{
    private readonly IDeadLettersClient _letters = Substitute.For<IDeadLettersClient>();
    private readonly NavigationManager _navigation;
    private readonly Guid _first = Guid.Parse("dddddddd-1111-0000-0000-000000000001");
    private readonly Guid _second = Guid.Parse("dddddddd-1111-0000-0000-000000000002");

    public DeadLettersPageTests()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Page(
            TestData.DeadLetter(EmailKinds.AgentReply, "a***@example.com", TestData.TicketId, 5, "smtp-transient", _first),
            TestData.DeadLetter(EmailKinds.NewTicketAlert, "s***@orbitly.test", null, 3, "<b>550 mailbox unavailable</b> " + new string('x', 200), _second))));
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        Services.AddSingleton(_letters);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private static PagedResponse<DeadLetterDto> Page(params DeadLetterDto[] items) => new(items, 1, DeadLettersCopy.PageSize, items.Length);

    private FailedEmailCounter Counter => Services.GetRequiredService<FailedEmailCounter>();

    private IRenderedComponent<DeadLettersPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/ops/dead-letters{query}");
        return Render<DeadLettersPage>();
    }

    private int Calls(string method) => _letters.ReceivedCalls().Count(c => c.GetMethodInfo().Name == method);

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<DeadLettersPage> cut, Guid id) => cut.Find($"tr[data-letter='{id}']");

    private static AngleSharp.Dom.IElement DiscardDialog(IRenderedComponent<DeadLettersPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent == "Discard this failed email?");

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<DeadLettersPage> cut) => DiscardDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // ---- the rows ------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_row_shows_the_masked_recipient_kind_ticket_link_tries_last_error_category_and_created_time()
    {
        var cut = RenderPage();

        var row = Row(cut, _first);
        row.Children[0].TextContent.ShouldBe("a***@example.com");
        row.Children[1].TextContent.ShouldBe("Agent reply");
        row.Children[2].QuerySelector("a")!.GetAttribute("href").ShouldBe($"/tickets/{TestData.TicketId}");
        row.Children[3].TextContent.ShouldBe("5");
        row.Children[4].TextContent.ShouldBe("Temporary SMTP problem");
        row.Children[5].TextContent.ShouldBe("3 h ago");
        Row(cut, _second).Children[2].QuerySelectorAll("a").ShouldBeEmpty();
        cut.Find("h1").TextContent.ShouldBe("Failed emails");
    }

    [Fact]
    public void Free_text_in_the_last_error_is_cut_short_and_encoded_never_markup()
    {
        var cut = RenderPage();

        var error = Row(cut, _second).Children[4];
        error.TextContent.Length.ShouldBeLessThanOrEqualTo(80);
        error.TextContent.ShouldEndWith("\u2026");
        error.QuerySelectorAll("b").ShouldBeEmpty();
        cut.Markup.ShouldContain("&lt;b&gt;550 mailbox unavailable");
    }

    [Theory]
    [InlineData("smtp-transient", "Temporary SMTP problem")]
    [InlineData("smtp-permanent", "SMTP refused the email")]
    [InlineData("smtp-authentication", "SMTP sign-in failed")]
    [InlineData("smtp-timeout", "SMTP timed out")]
    [InlineData("smtp-unknown", "Unknown SMTP error")]
    [InlineData(null, "No error recorded")]
    [InlineData("", "No error recorded")]
    [InlineData("smtp-transient; worker lease expired", "Temporary SMTP problem")]
    [InlineData("smtp-timeout; worker lease expired", "SMTP timed out")]
    [InlineData("worker lease expired", "worker lease expired")]
    public void The_last_error_category_has_plain_words(string? lastError, string expected)
    {
        DeadLettersCopy.ErrorLabel(lastError).ShouldBe(expected);
    }

    [Fact]
    public void No_failed_emails_is_a_positive_plain_empty_state()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(Page()));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No failed emails");
        cut.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again_and_refreshes_the_badge()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<DeadLetterDto>>("api-error", "The API is unavailable."),
            TestData.Ok(Page(TestData.DeadLetter(id: _first))));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the failed emails. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void The_first_page_asks_for_25_and_a_page_in_the_query_string_asks_for_that_page()
    {
        RenderPage("?page=2");

        _letters.Received(1).ListAsync(2, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 1, 25, 60)));
        var cut = RenderPage();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/ops/dead-letters?page=2");
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _letters.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- retry: no confirmation, once ----------------------------------------------------------------------------

    [Fact]
    public void Retry_needs_no_confirmation_calls_retry_once_for_that_row_reads_the_list_again_and_the_badge_follows()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first), TestData.DeadLetter(id: _second))),
            TestData.Ok(Page(TestData.DeadLetter(id: _second))));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        _letters.Received(1).RetryAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(1);
        Counter.Count.ShouldBe(1, "the badge follows the total of the list that was read again");
        StatusMessages.Current.ShouldBe("Queued a retry for the agent reply email");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void A_retry_that_fails_again_comes_back_in_the_list_with_its_new_error()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first, lastError: "smtp-transient"))),
            TestData.Ok(Page(TestData.DeadLetter(id: _first, attempts: 6, lastError: "smtp-permanent"))));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Row(cut, _first).Children[3].TextContent.ShouldBe("6");
        Row(cut, _first).Children[4].TextContent.ShouldBe("SMTP refused the email");
    }

    [Fact]
    public void Two_clicks_on_retry_while_the_first_runs_send_one_request()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();
        cut.FindAll("button.ts-retry, button.ts-discard").ShouldAllBe(b => b.HasAttribute("disabled"));
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2));
    }

    [Fact]
    public void A_failed_retry_says_nothing_changed_and_keeps_the_row_and_the_badge_as_they_were()
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't retry the email. Nothing was changed. The API refused.");
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
        Counter.Count.ShouldBe(2, "a failed write changed nothing, so the badge still shows the list that was read first");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_retry_answer_never_claims_nothing_changed_and_offers_a_reload_that_refreshes_the_badge()
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The retry may have been queued.");
        alert.TextContent.ShouldNotContain("Nothing was changed");

        alert.QuerySelector("button")!.Click();

        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
    }

    [Theory]
    [InlineData(ApiErrorCodes.OutboxNotFound, "That email isn't in the list any more.")]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered, "That email was already retried or discarded.")]
    public void An_email_that_is_already_handled_says_so_and_reads_the_list_again_and_the_badge_follows(string code, string message)
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(code, "No such row.", ResultErrorKind.NotFound));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        StatusMessages.Current.ShouldBe(message);
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
    }

    // ---- discard: asks first, once -------------------------------------------------------------------------------

    [Fact]
    public void Discard_asks_first_naming_the_email_and_the_consequence_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        var dialog = DiscardDialog(cut);
        dialog.TextContent.ShouldContain("Agent reply to a***@example.com.");
        dialog.TextContent.ShouldContain("TechStrap will stop trying to send it, and it will not be sent. This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        Confirm(cut).TextContent.ShouldBe("Discard email");
        Confirm(cut).ClassName!.ShouldContain("btn-danger");
        Dialogs.VerifyInvoke("open", 1);
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(0);
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(0);
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
    }

    [Fact]
    public void Confirming_discards_that_email_once_reads_the_list_again_and_the_badge_follows_and_says_so()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first), TestData.DeadLetter(id: _second))),
            TestData.Ok(Page(TestData.DeadLetter(id: _second))));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        _letters.Received(1).DiscardAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(1);
        Counter.Count.ShouldNotBeNull();
        StatusMessages.Current.ShouldBe("Discarded the agent reply email");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_double_click_on_confirm_discards_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();
        DiscardDialog(cut).QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        DiscardDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public void Choosing_a_different_row_names_that_row_and_discards_that_one()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, _second).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).TextContent.ShouldContain("New ticket alert to s***@orbitly.test.");
        Confirm(cut).Click();

        _letters.Received(1).DiscardAsync(_second, Arg.Any<CancellationToken>());
        _letters.DidNotReceive().DiscardAsync(_first, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_failed_discard_keeps_the_dialog_open_and_says_nothing_was_changed()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        DiscardDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't discard the email. Nothing was changed. The API refused.");
        Dialogs.VerifyNotInvoke("close");
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
    }

    [Fact]
    public void A_lost_discard_answer_never_claims_nothing_changed_blocks_a_second_discard_and_offers_a_reload()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        Confirm(cut).Click();

        var dialog = DiscardDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The discard may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);

        DiscardDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_discard_of_an_email_that_is_already_gone_closes_the_dialog_says_so_and_refreshes()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.OutboxNotFound, "No such row.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That email isn't in the list any more.");
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_discard_that_finishes_after_the_page_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        Confirm(cut).Click();

        cut.FindComponent<DeadLettersContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _letters.Received(1).DiscardAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(1);
    }
    // ---- an unknown outcome is held until the list has been read again --------------------------------------------

    [Fact]
    public void After_a_lost_retry_answer_a_second_retry_of_that_row_sends_nothing_until_the_list_has_been_read_again()
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The retry may have been queued.");

        cut.Find(".ts-conflict button").Click();
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(2);
    }

    [Fact]
    public void A_read_that_started_before_the_lost_retry_answer_does_not_release_the_hold()
    {
        var gate = new TaskCompletionSource<Result<PagedResponse<DeadLetterDto>>>();
        _letters.ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        _navigation.NavigateTo("/ops/dead-letters?page=2");
        cut.WaitForAssertion(() => _letters.Received(1).ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()));

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();
        gate.SetResult(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 2, DeadLettersCopy.PageSize, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-letter]").Count.ShouldBe(1));
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The retry may have been queued.");
    }

    [Fact]
    public void After_a_lost_discard_answer_asking_to_discard_that_row_again_shows_the_uncertain_copy_and_sends_nothing()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        Confirm(cut).Click();
        DiscardDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        DiscardDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("The discard may have gone through. Reload the list to check before you try again.");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);
    }

    // ---- stale loads and pages past the end ----------------------------------------------------------------------

    [Fact]
    public void A_slow_answer_for_an_earlier_page_never_replaces_the_newest_load_or_the_badge()
    {
        var older = new TaskCompletionSource<Result<PagedResponse<DeadLetterDto>>>();
        var newer = new TaskCompletionSource<Result<PagedResponse<DeadLetterDto>>>();
        _letters.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(older.Task);
        _letters.ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(newer.Task);
        var cut = RenderPage();
        _navigation.NavigateTo("/ops/dead-letters?page=2");
        cut.WaitForAssertion(() => Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2));

        newer.SetResult(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _second)], 2, 25, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-letter]").Count.ShouldBe(1));
        older.SetResult(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first), TestData.DeadLetter()], 1, 25, 3)));

        cut.FindAll("tr[data-letter]").Select(r => r.GetAttribute("data-letter")).ShouldBe([_second.ToString()]);
        Counter.Count.ShouldBe(26, "the badge follows the newest load, not the one that finished last");
    }

    [Fact]
    public void A_retry_that_finishes_after_the_page_is_gone_is_not_cancelled_and_changes_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        cut.FindComponent<DeadLettersContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _letters.Received(1).RetryAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(1);
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_page_with_rows_and_never_says_there_are_no_failed_emails()
    {
        _letters.ListAsync(99, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<DeadLetterDto>([], 99, 25, 30)));
        _letters.ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 2, 25, 30)));

        var cut = RenderPage("?page=99");

        cut.WaitForAssertion(() => cut.FindAll("tr[data-letter]").Count.ShouldBe(1));
        _navigation.Uri.ShouldEndWith("/ops/dead-letters?page=2");
        _letters.Received(1).ListAsync(2, 25, Arg.Any<CancellationToken>());
        cut.Markup.ShouldNotContain("No failed emails");
        Counter.Count.ShouldBe(30);
    }

    [Fact]
    public void Discarding_the_last_row_of_the_last_page_moves_to_the_page_before_it()
    {
        _letters.ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 2, 25, 26)),
            TestData.Ok(new PagedResponse<DeadLetterDto>([], 2, 25, 25)));
        _letters.ListAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _second)], 1, 25, 25)));
        var cut = RenderPage("?page=2");
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll("tr[data-letter]").Count.ShouldBe(1));
        _navigation.Uri.ShouldEndWith("/ops/dead-letters");
        Row(cut, _second).ShouldNotBeNull();
        cut.Markup.ShouldNotContain("No failed emails");
    }
}
