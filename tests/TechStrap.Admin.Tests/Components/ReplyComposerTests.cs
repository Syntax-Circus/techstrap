using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class ReplyComposerTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private int _sent;
    private int _conflicts;
    private int _gone;
    private AgentMessageResponse? _lastSent;

    public ReplyComposerTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Public));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Internal));
    }

    private static Result<AgentMessageResponse> Accepted(string visibility) =>
        TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, visibility), TestData.State(rowVersion: 8)));

    private IRenderedComponent<ReplyComposer> RenderComposer(uint rowVersion = 7, Guid? ticketId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, ticketId ?? TestData.TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.RowVersion, rowVersion)
            .Add(c => c.OnSent, response => { _sent++; _lastSent = response; })
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++));

    private static void Pick(IRenderedComponent<ReplyComposer> cut, string name, int bytes = 4, string type = "image/png") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, null, type));

    private static void PickMany(IRenderedComponent<ReplyComposer> cut, params string[] names) =>
        cut.FindComponent<InputFile>().UploadFiles(names.Select(n => InputFileContent.CreateFromBinary(new byte[4], n, null, "image/png")).ToArray());

    private DraftStore Drafts => Services.GetRequiredService<DraftStore>();

    private IEnumerable<AddAgentReplyRequest> ReplyRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).Select(c => (AddAgentReplyRequest)c.GetArguments()[1]!);

    private IEnumerable<AddInternalNoteRequest> NoteRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.AddNoteAsync)).Select(c => (AddInternalNoteRequest)c.GetArguments()[1]!);

    [Fact]
    public void A_public_reply_is_the_default_with_the_audience_and_the_plain_warning()
    {
        var cut = RenderComposer();

        var tabs = cut.FindAll(".ts-composer-modes button");
        tabs[0].GetAttribute("aria-pressed").ShouldBe("true");
        tabs[1].GetAttribute("aria-pressed").ShouldBe("false");
        tabs[0].TextContent.ShouldContain("Public reply");
        tabs[1].TextContent.ShouldContain("Internal note");
        cut.Find("p.ts-composer-audience").TextContent.ShouldBe("To: ada@example.com");
        cut.Find("p.ts-composer-warning").TextContent.ShouldBe("PUBLIC: this will be emailed to the customer.");
        cut.FindAll("p.ts-composer-warning--internal").ShouldBeEmpty();
        cut.Find("section.ts-composer").ClassList.ShouldContain("ts-composer--public");
        cut.Find("section.ts-composer").GetAttribute("data-shortcut-scope").ShouldBe("composer");
        cut.Find("textarea").GetAttribute("placeholder").ShouldBe("Write your reply");
        cut.FindAll(".ts-composer-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Send reply CtrlEnter", "Send and solve"]);
        cut.Find("select").InnerHtml.ShouldContain("Set to Pending");
    }

    [Fact]
    public void The_internal_note_mode_has_the_exact_warning_a_status_role_and_none_of_the_public_controls()
    {
        var cut = RenderComposer();

        cut.FindAll(".ts-composer-modes button")[1].Click();

        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("false");
        var warning = cut.Find("p.ts-composer-warning--internal");
        warning.TextContent.ShouldBe("INTERNAL: the customer will NOT see this note.");
        warning.GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-composer-audience").TextContent.ShouldBe("Visible to agents only");
        cut.Find("textarea").GetAttribute("placeholder").ShouldBe("Note for the team only");
        cut.Find(".ts-composer-actions button").TextContent.ShouldContain("Add internal note");
        cut.Find("section.ts-composer").ClassList.ShouldContain("ts-composer--note");
        cut.FindAll("select").ShouldBeEmpty();
        cut.Find(".ts-composer-files").HasAttribute("hidden").ShouldBeTrue("the picker stays mounted (its file handles live in the element) but is hidden");
        cut.Markup.ShouldNotContain("Send and solve");
        cut.Markup.ShouldNotContain("PUBLIC:");
    }

    [Fact]
    public void Each_mode_keeps_its_own_text_and_switching_never_moves_or_converts_it()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Public words");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.Find("textarea").Input("Team words");

        cut.FindAll(".ts-composer-modes button")[0].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Public words");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Team words");
    }

    [Fact]
    public async Task Drafts_survive_the_screen_being_rebuilt_and_are_separate_per_ticket()
    {
        var first = RenderComposer();
        first.Find("textarea").Input("Half-written reply");
        first.FindAll(".ts-composer-modes button")[1].Click();
        first.Find("textarea").Input("Half-written note");
        await DisposeComponentsAsync();

        var again = RenderComposer();
        again.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        again.Find("textarea").GetAttribute("value").ShouldBe("Half-written note");
        again.FindAll(".ts-composer-modes button")[0].Click();
        again.Find("textarea").GetAttribute("value").ShouldBe("Half-written reply");

        var other = RenderComposer(ticketId: Guid.NewGuid());
        other.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        other.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public void A_public_reply_calls_the_reply_method_with_the_text_the_status_and_the_current_row_version()
    {
        var cut = RenderComposer(rowVersion: 7);
        cut.Find("textarea").Input("Try resetting your password.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var request = ReplyRequests().Single();
        (request.Body, request.StatusAfter, request.RowVersion).ShouldBe(("Try resetting your password.", "Pending", (uint?)7));
        request.LinkedArticleIds.ShouldBeEmpty();
        NoteRequests().ShouldBeEmpty();
    }

    [Fact]
    public void Send_and_solve_asks_for_Solved_and_the_leave_unchanged_choice_asks_for_no_status()
    {
        var solve = RenderComposer();
        solve.Find("textarea").Input("Fixed in 2.3.2.");
        solve.FindAll(".ts-composer-actions button")[1].Click();
        ReplyRequests().Single().StatusAfter.ShouldBe("Solved");

        _tickets.ClearReceivedCalls();
        var keep = RenderComposer(ticketId: Guid.NewGuid());
        keep.Find("textarea").Input("Looking into it.");
        keep.Find("select").Change(ReplyComposerCopy.LeaveUnchangedValue);
        keep.FindAll(".ts-composer-actions button")[0].Click();
        ReplyRequests().Single().StatusAfter.ShouldBeNull();
    }

    [Fact]
    public void An_internal_note_calls_the_note_method_with_the_current_row_version_and_never_the_reply_method()
    {
        var cut = RenderComposer(rowVersion: 7);
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Customer is on the legacy plan.");

        cut.Find(".ts-composer-actions button").Click();

        var request = NoteRequests().Single();
        (request.Body, request.RowVersion).ShouldBe(("Customer is on the legacy plan.", (uint?)7));
        ReplyRequests().ShouldBeEmpty();
    }

    [Fact]
    public void The_reply_carries_the_picked_files_and_a_write_token_that_a_closing_screen_cannot_cancel()
    {
        var cut = RenderComposer();
        Pick(cut, "screenshot.png");
        cut.Find("textarea").Input("See attached.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var call = _tickets.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync));
        ((IReadOnlyList<ReplyAttachment>)call.GetArguments()[2]!).Select(f => f.FileName).ShouldBe(["screenshot.png"]);
        ((CancellationToken)call.GetArguments()[3]!).CanBeCanceled.ShouldBeFalse();
    }

    [Fact]
    public void Double_submit_is_prevented_and_the_controls_are_disabled_while_a_request_is_in_flight()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("Once only.");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.FindAll(".ts-composer-actions button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find("textarea").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll(".ts-composer-actions button")[0].Click();

        ReplyRequests().Count().ShouldBe(1);

        gate.SetResult(Accepted(MessageVisibilities.Public));
        cut.WaitForAssertion(() => cut.FindAll(".ts-composer-actions button").ShouldAllBe(b => !b.HasAttribute("disabled")));
        ReplyRequests().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Pressing_the_send_shortcut_twice_quickly_sends_exactly_one_request()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("Once only.");

        var first = PressAsync("Enter", ctrl: true, typing: true, scope: "composer");
        var second = PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        ReplyRequests().Count().ShouldBe(1);
        gate.SetResult(Accepted(MessageVisibilities.Public));
        await Task.WhenAll(first, second);
        cut.WaitForAssertion(() => _sent.ShouldBe(1));
        ReplyRequests().Count().ShouldBe(1);
    }

    [Fact]
    public void An_accepted_reply_clears_the_reply_and_its_files_only_and_reports_once()
    {
        var cut = RenderComposer();
        Pick(cut, "a.png");
        cut.Find("textarea").Input("Reply text");
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Note text");
        cut.FindAll(".ts-composer-modes button")[0].Click();

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.FindAll(".ts-file-list").ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        _sent.ShouldBe(1);
        _lastSent!.Ticket.RowVersion.ShouldBe(8u);
        StatusMessages.Current.ShouldBe("Reply sent on ORB-42");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Note text");
    }

    [Fact]
    public void An_accepted_note_clears_the_note_says_so_in_the_status_bar_and_stays_in_note_mode()
    {
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Escalated to billing.");

        cut.Find(".ts-composer-actions button").Click();

        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        StatusMessages.Current.ShouldBe("Internal note added to ORB-42");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        _sent.ShouldBe(1);
    }

    [Fact]
    public void A_failed_send_keeps_the_text_and_the_files_shows_why_and_a_retry_sends_the_same_files_again()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>("boom", "The API is unavailable."), Accepted(MessageVisibilities.Public));
        var cut = RenderComposer();
        Pick(cut, "a.png");
        cut.Find("textarea").Input("Reply text");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't send the reply. Your text is kept, and your files stay attached while you stay on this ticket. The API is unavailable.");
        cut.Find("textarea").GetAttribute("value").ShouldBe("Reply text");
        cut.Find(".ts-file-name").TextContent.ShouldBe("a.png");
        _sent.ShouldBe(0);

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _sent.ShouldBe(1);
        var files = _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync))
            .Select(c => ((IReadOnlyList<ReplyAttachment>)c.GetArguments()[2]!).Single()).ToList();
        files.Count.ShouldBe(2);
        files.Select(f => f.FileName).ShouldBe(["a.png", "a.png"]);
        using var first = files[0].OpenRead();
        using var second = files[1].OpenRead();
        second.Length.ShouldBe(4, "the retry reopens the same picked file");
        second.ShouldNotBeSameAs(first, "each attempt reads a fresh stream");
        second.Position.ShouldBe(0);
    }

    [Fact]
    public void Draft_and_files_survive_a_conflict()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict), Accepted(MessageVisibilities.Public));
        var cut = RenderComposer(rowVersion: 7);
        Pick(cut, "log.txt", type: "text/plain");
        cut.Find("textarea").Input("A long reply that must not be lost.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _conflicts.ShouldBe(1);
        _sent.ShouldBe(0);
        cut.Find("[role=alert]").TextContent.ShouldBe("This ticket changed since you opened it. Your text is kept, and your files stay attached while you stay on this ticket; reload the ticket, then send again.");
        cut.Find("textarea").GetAttribute("value").ShouldBe("A long reply that must not be lost.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");

        // The page reloads and hands the composer the new RowVersion; the retry sends the SAME draft against it.
        cut.Render(p => p.Add(c => c.RowVersion, 9u));
        cut.FindAll(".ts-composer-actions button")[0].Click();

        ReplyRequests().Select(r => r.RowVersion).ShouldBe([7u, 9u]);
        ReplyRequests().Select(r => r.Body).Distinct().ShouldBe(["A long reply that must not be lost."]);
        _sent.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void A_note_conflict_also_keeps_the_note_and_raises_the_conflict_once()
    {
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Do not lose me.");

        cut.Find(".ts-composer-actions button").Click();

        _conflicts.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBe("Do not lose me.");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public void A_closed_ticket_shows_the_servers_message_and_a_deleted_ticket_raises_gone_and_both_keep_the_text()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(
                TestData.Fail<AgentMessageResponse>(ApiErrorCodes.TicketClosed, "This ticket is closed.", ResultErrorKind.Conflict),
                TestData.Fail<AgentMessageResponse>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderComposer();
        cut.Find("textarea").Input("Keep this.");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("This ticket is closed.");
        _conflicts.ShouldBe(0);

        cut.FindAll(".ts-composer-actions button")[0].Click();
        _gone.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBe("Keep this.");
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiTimeout)]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    [InlineData(ApiErrorCodes.UnexpectedResponse)]
    [InlineData(ApiErrorCodes.ApiError)]
    public void An_uncertain_write_failure_says_the_reply_may_have_been_sent_and_keeps_the_draft_and_files(string code)
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(code, "TechStrap took too long to answer. Try again."));
        var cut = RenderComposer();
        Pick(cut, "a.png");
        cut.Find("textarea").Input("Maybe sent.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var alert = cut.Find("[role=alert]").TextContent;
        alert.ShouldBe("The reply may already have been sent. Your text is kept, and your files stay attached while you stay on this ticket. Check the timeline before sending again.");
        alert.ShouldNotContain("Try again");
        cut.Find("textarea").GetAttribute("value").ShouldBe("Maybe sent.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("a.png");
        _sent.ShouldBe(0);
    }

    [Fact]
    public void An_uncertain_note_failure_says_the_note_may_have_been_added_and_keeps_the_note()
    {
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Maybe added.");

        cut.Find(".ts-composer-actions button").Click();

        var alert = cut.Find("[role=alert]").TextContent;
        alert.ShouldBe("The note may already have been added. Your text is kept. Check the timeline before adding it again.");
        alert.ShouldNotContain("Try again");
        cut.Find("textarea").GetAttribute("value").ShouldBe("Maybe added.");
    }

    [Fact]
    public void An_empty_reply_or_note_is_refused_before_any_call()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("   ");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("Write a reply before sending.");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find(".ts-composer-actions button").Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("Write a note before adding it.");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Picked_files_are_listed_with_their_size_and_can_be_removed()
    {
        var cut = RenderComposer();
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(new byte[2048], "a.png", null, "image/png"),
            InputFileContent.CreateFromBinary(new byte[10], "b.pdf", null, "application/pdf"));

        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["a.png", "b.pdf"]);
        cut.FindAll(".ts-file-size").First().TextContent.ShouldBe("(2 KB)");

        cut.Find("button[aria-label='Remove a.png']").Click();

        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["b.pdf"]);
    }

    [Fact]
    public void A_file_that_is_too_large_or_the_wrong_type_or_over_the_count_is_refused_with_a_reason()
    {
        var cut = RenderComposer();

        Pick(cut, "huge.png", checked((int)IntakeLimits.MaxFileBytes + 1));
        cut.Find("[role=alert]").TextContent.ShouldBe("huge.png is larger than 10 MB.");
        cut.FindAll(".ts-file-list").ShouldBeEmpty();

        Pick(cut, "run.exe");
        cut.Find("[role=alert]").TextContent.ShouldBe("run.exe: this file type isn't allowed.");

        PickMany(cut, Enumerable.Range(0, IntakeLimits.MaxFiles + 1).Select(i => $"f{i}.png").ToArray());
        cut.Find("[role=alert]").TextContent.ShouldBe("You can attach up to 5 files.");
        cut.FindAll(".ts-file-name").Count.ShouldBe(IntakeLimits.MaxFiles);
    }

    [Fact]
    public void A_second_pick_replaces_the_file_list_because_the_browser_drops_the_first_picks_handles()
    {
        var cut = RenderComposer();

        PickMany(cut, "a.png", "b.png");
        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["a.png", "b.png"]);

        PickMany(cut, "c.png");
        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["c.png"]);
    }

    [Fact]
    public void Switching_modes_keeps_the_same_file_input_and_its_file_list()
    {
        var cut = RenderComposer();
        PickMany(cut, "a.png");
        var input = cut.FindComponent<InputFile>().Instance;

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.FindAll(".ts-composer-modes button")[0].Click();

        cut.FindComponent<InputFile>().Instance.ShouldBeSameAs(input);
        cut.Find(".ts-file-name").TextContent.ShouldBe("a.png");
        cut.Find(".ts-composer-files").HasAttribute("hidden").ShouldBeFalse();
    }

    [Fact]
    public async Task Remounting_the_composer_keeps_the_text_drops_the_files_and_says_so()
    {
        var first = RenderComposer();
        first.Find("textarea").Input("Keep my words.");
        PickMany(first, "a.png");
        first.FindAll("p.ts-composer-notice").ShouldBeEmpty();
        await DisposeComponentsAsync();

        var again = RenderComposer();

        again.Find("textarea").GetAttribute("value").ShouldBe("Keep my words.");
        again.FindAll(".ts-file-list").ShouldBeEmpty();
        again.Find("p.ts-composer-notice").TextContent.ShouldBe("Your attachments were removed when you left this ticket. Attach them again.");
    }

    [Fact]
    public async Task Remounting_without_files_shows_no_notice()
    {
        var first = RenderComposer();
        first.Find("textarea").Input("Words only.");
        await DisposeComponentsAsync();

        RenderComposer().FindAll("p.ts-composer-notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_mid_send_does_not_cancel_the_write_and_an_accepted_send_still_clears_the_draft()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        CancellationToken token = default;
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Do<CancellationToken>(t => token = t)).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("In flight.");
        cut.FindAll(".ts-composer-actions button")[0].Click();

        await DisposeComponentsAsync();
        token.IsCancellationRequested.ShouldBeFalse();
        gate.SetResult(Accepted(MessageVisibilities.Public));

        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).PublicText.ShouldBeEmpty());
        StatusMessages.Current.ShouldBe("Reply sent on ORB-42");
        RenderComposer().Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void An_unmapped_exception_during_a_send_is_uncertain_and_logs_only_the_exception_type()
    {
        var logs = new List<(LogLevel Level, string Message, Exception? Exception)>();
        Services.AddSingleton<ILogger<ReplyComposer>>(new ListLogger(logs));
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<AgentMessageResponse>>>(_ => throw new InvalidOperationException("secret ada@example.com"));
        var cut = RenderComposer();
        cut.Find("textarea").Input("Maybe sent.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).UncertainSend.ShouldBe(ComposerMode.PublicReply));
        var entry = logs.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(nameof(InvalidOperationException));
        entry.Message.ShouldNotContain("ada@example.com");
        entry.Exception.ShouldBeNull();
    }

    private sealed class ListLogger(List<(LogLevel Level, string Message, Exception? Exception)> logs) : ILogger<ReplyComposer>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Add((logLevel, formatter(state, exception), exception));
    }

    [Fact]
    public async Task A_send_that_fails_uncertainly_after_leaving_shows_the_check_the_timeline_notice_on_the_next_mount()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("Maybe sent.");
        cut.FindAll(".ts-composer-actions button")[0].Click();

        await DisposeComponentsAsync();
        gate.SetResult(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).UncertainSend.ShouldNotBeNull());

        var again = RenderComposer();
        again.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent.");
        again.Find("textarea").GetAttribute("value").ShouldBe("Maybe sent.");
    }

    [Fact]
    public void The_mode_buttons_are_disabled_while_a_request_is_in_flight()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("Wait.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.FindAll(".ts-composer-modes button").ShouldAllBe(b => b.HasAttribute("disabled"));
        gate.SetResult(Accepted(MessageVisibilities.Public));
        cut.WaitForAssertion(() => cut.FindAll(".ts-composer-modes button").ShouldAllBe(b => !b.HasAttribute("disabled")));
    }

    [Fact]
    public void Switching_modes_keeps_the_check_the_timeline_warning_until_the_next_send()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ApiTimeout, "x"), Accepted(MessageVisibilities.Public));
        var cut = RenderComposer();
        cut.Find("textarea").Input("Maybe sent.");
        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent.");
        cut.FindAll(".ts-composer-modes button")[0].Click();
        cut.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent.");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void The_file_picker_offers_only_the_allowed_extensions()
    {
        var cut = RenderComposer();

        cut.Find("input[type=file]").GetAttribute("accept").ShouldBe(".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip");
        cut.Find("input[type=file]").HasAttribute("multiple").ShouldBeTrue();
    }

    [Fact]
    public void A_browser_file_name_with_quotes_or_path_separators_is_cleaned_before_it_is_sent()
    {
        var cut = RenderComposer();
        Pick(cut, "my \"shot\"\\..\\evil/ one.png");
        cut.Find("textarea").Input("Cleaned names.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var call = _tickets.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync));
        var name = ((IReadOnlyList<ReplyAttachment>)call.GetArguments()[2]!).Single().FileName;
        name.ShouldBe("my shot..evil one.png");
        name.IndexOfAny(['"', '\\', '/']).ShouldBe(-1);
    }

    [Theory]
    [InlineData("", "attachment")]
    [InlineData("   ", "attachment")]
    [InlineData("\"\"", "attachment")]
    [InlineData("/\\", "attachment")]
    [InlineData("\t\r\n", "attachment")]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("C:\\temp\\report.pdf", "C:tempreport.pdf")]
    [InlineData("a\"b'c\u0001d.log", "abcd.log")]
    [InlineData("  spaced.png  ", "spaced.png")]
    [InlineData("evil\u202Egnp.exe", "evilgnp.exe")]
    [InlineData(".png", ".png")]
    public void The_attachment_name_cleaner_never_returns_an_empty_or_odd_name_and_keeps_the_extension(string raw, string expected)
    {
        AttachmentFileName.Clean(raw).ShouldBe(expected);
    }

    [Fact]
    public async Task R_and_n_switch_the_tab_and_focus_the_box_and_the_mode_choice_is_kept()
    {
        var cut = RenderComposer();
        // bUnit blanks blazor:elementReference on the next render, so read the box's reference id before the key press and compare ids.
        var boxId = cut.Find("textarea").GetAttribute("blazor:elementReference");

        await PressAsync("n");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id.ShouldBe(boxId);

        await PressAsync("r");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task Ctrl_enter_inside_the_composer_sends_in_the_current_mode_and_outside_it_does_nothing()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Sent with the keyboard.");

        await PressAsync("Enter", ctrl: true, typing: true, scope: null);
        _tickets.ReceivedCalls().ShouldBeEmpty();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        ReplyRequests().Select(r => r.Body).ShouldBe(["Sent with the keyboard."]);
        cut.WaitForAssertion(() => _sent.ShouldBe(1));
    }

    [Fact]
    public async Task Ctrl_enter_in_note_mode_adds_the_note()
    {
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Note by keyboard.");

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        NoteRequests().Select(r => r.Body).ShouldBe(["Note by keyboard."]);
        ReplyRequests().ShouldBeEmpty();
    }

    [Fact]
    public void Leaving_the_page_asks_for_confirmation_only_while_there_is_unsent_text()
    {
        var cut = RenderComposer();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();

        cut.Find("textarea").Input("Unsent");

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();
    }

    [Fact]
    public async Task A_disposed_composer_no_longer_answers_shortcuts()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Text");
        await DisposeComponentsAsync();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    private static Result<AgentMessageResponse> TimeoutFailure() =>
        TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again.");

    private TaskCompletionSource<Result<AgentMessageResponse>> HoldReplies()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        return gate;
    }

    [Fact]
    public async Task A_late_success_keeps_text_that_differs_from_what_was_sent()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("First reply.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();
        Drafts.Get(TestData.TicketId).PublicText = "Newer reply.";

        gate.SetResult(Accepted(MessageVisibilities.Public));

        a.WaitForAssertion(() => Drafts.Get(TestData.TicketId).InFlight.ShouldBeFalse());
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Newer reply.");
    }

    [Fact]
    public async Task A_late_success_clears_text_that_still_equals_what_was_sent()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("Same reply.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();

        gate.SetResult(Accepted(MessageVisibilities.Public));

        a.WaitForAssertion(() => Drafts.Get(TestData.TicketId).InFlight.ShouldBeFalse());
        Drafts.Get(TestData.TicketId).PublicText.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_composer_mounted_while_a_write_is_in_flight_shows_sending_with_send_and_modes_disabled()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("In flight.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();

        var b = RenderComposer();

        b.Find("p.ts-composer-sending").TextContent.ShouldBe("Sending your reply...");
        b.Find("p.ts-composer-sending").GetAttribute("role").ShouldBe("status");
        b.FindAll(".ts-composer-actions button").ShouldAllBe(x => x.HasAttribute("disabled"));
        b.FindAll(".ts-composer-modes button").ShouldAllBe(x => x.HasAttribute("disabled"));
        gate.SetResult(Accepted(MessageVisibilities.Public));
        b.WaitForAssertion(() => b.FindAll("p.ts-composer-sending").ShouldBeEmpty());
    }

    [Fact]
    public async Task A_mounted_composer_learns_a_success_that_settles_later_and_re_enables_send()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("In flight.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();
        var b = RenderComposer();

        gate.SetResult(Accepted(MessageVisibilities.Public));

        b.WaitForAssertion(() => b.FindAll(".ts-composer-actions button").ShouldAllBe(x => !x.HasAttribute("disabled")));
        b.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        b.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mounted_composer_learns_an_uncertain_failure_that_settles_later()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("In flight.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();
        var b = RenderComposer();

        gate.SetResult(TimeoutFailure());

        b.WaitForAssertion(() => b.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent."));
        b.Find("textarea").GetAttribute("value").ShouldBe("In flight.");
        b.FindAll(".ts-composer-actions button").ShouldAllBe(x => !x.HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_composer_mounted_during_an_in_flight_write_cannot_start_a_second_send()
    {
        HoldReplies();
        var a = RenderComposer();
        a.Find("textarea").Input("Once.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();
        RenderComposer();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        ReplyRequests().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stream_read_failure_settles_the_draft_as_uncertain_and_never_escapes(bool disposedFirst)
    {
        var gate = HoldReplies();
        var cut = RenderComposer();
        cut.Find("textarea").Input("Stale input.");
        cut.FindAll(".ts-composer-actions button")[0].Click();
        if (disposedFirst)
        {
            await DisposeComponentsAsync();
        }

        gate.SetException(new JSException("The input element is gone."));

        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).InFlight.ShouldBeFalse());
        Drafts.Get(TestData.TicketId).UncertainSend.ShouldBe(ComposerMode.PublicReply);
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Stale input.");
        if (!disposedFirst)
        {
            cut.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent.");
            cut.FindAll(".ts-composer-actions button").ShouldAllBe(x => !x.HasAttribute("disabled"));
        }
    }

    [Fact]
    public void A_circuit_loss_during_a_note_settles_the_note_as_uncertain()
    {
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<AgentMessageResponse>>>(_ => throw new JSDisconnectedException("gone"));
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Note.");

        cut.Find(".ts-composer-actions button").Click();

        Drafts.Get(TestData.TicketId).UncertainSend.ShouldBe(ComposerMode.InternalNote);
        Drafts.Get(TestData.TicketId).InFlight.ShouldBeFalse();
        cut.Find("[role=alert]").TextContent.ShouldStartWith("The note may already have been added.");
    }

    [Fact]
    public async Task An_uncertain_result_with_dropped_files_does_not_claim_the_files_stay_attached_and_the_notice_shows_in_either_mode()
    {
        var gate = HoldReplies();
        var a = RenderComposer();
        PickMany(a, "a.png");
        a.Find("textarea").Input("With a file.");
        a.FindAll(".ts-composer-actions button")[0].Click();
        await DisposeComponentsAsync();
        gate.SetResult(TimeoutFailure());
        a.WaitForAssertion(() => Drafts.Get(TestData.TicketId).InFlight.ShouldBeFalse());

        var b = RenderComposer();

        b.Find("[role=alert]").TextContent.ShouldBe("The reply may already have been sent. Your text is kept, but your attachments were removed. Check the timeline before sending again.");
        b.FindAll(".ts-composer-modes button")[1].Click();
        b.Find("p.ts-composer-notice").TextContent.ShouldBe("Your attachments were removed when you left this ticket. Attach them again.");
        b.FindAll(".ts-composer-files p.ts-composer-notice").ShouldBeEmpty();
    }
}
