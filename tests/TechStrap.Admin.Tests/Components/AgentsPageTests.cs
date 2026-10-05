using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Agents;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The agents page. Roles are read-only (D-041): a badge and the identity-provider note, never an input. Review Focus 5: deactivating asks first and fires once; activating has no confirmation by
/// design and fires once. A 409 last-active-admin is shown inside the dialog and changes nothing.
/// </summary>
public sealed class AgentsPageTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly NavigationManager _navigation;

    public AgentsPageTests()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Page(
            TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin, lastSeen: TestData.Now.AddMinutes(-1)),
            TestData.AgentRow("Sam Ortiz", TestData.SamAgentId, lastSeen: TestData.Now.AddHours(-2)),
            TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false))));
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new AgentDto(call.Arg<Guid>(), "x", "x@example.com", AgentRoles.Agent, call.ArgAt<bool>(1), null, null)));
        Services.AddSingleton(_agents);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private static PagedResponse<AgentListItemDto> Page(params AgentListItemDto[] items) => new(items, 1, AgentsCopy.PageSize, items.Length);

    private IRenderedComponent<AgentsPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/settings/agents{query}");
        return Render<AgentsPage>();
    }

    private IEnumerable<(Guid Id, bool Active)> Writes() =>
        _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.SetActiveAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (bool)c.GetArguments()[1]!));

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<AgentsPage> cut, Guid id) => cut.Find($"tr[data-agent='{id}']");

    private static AngleSharp.Dom.IElement DeactivateDialog(IRenderedComponent<AgentsPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Deactivate ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<AgentsPage> cut) =>
        DeactivateDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // ---- the list, read-only roles -------------------------------------------------------------------------------

    [Fact]
    public void Each_agent_is_listed_with_email_role_status_and_last_seen_and_the_admin_is_marked_as_you()
    {
        var cut = RenderPage();

        var ada = Row(cut, TestData.AdaAgentId);
        ada.Children[0].TextContent.ShouldContain("Ada Admin");
        ada.Children[0].TextContent.ShouldContain("(you)");
        ada.Children[1].TextContent.ShouldBe("ada@example.com");
        ada.Children[2].TextContent.ShouldBe("Admin");
        ada.Children[3].TextContent.ShouldBe("Active");
        ada.Children[4].TextContent.ShouldBe("1 min ago");
        var rae = Row(cut, TestData.RaeAgentId);
        rae.Children[3].TextContent.ShouldBe("Inactive");
        rae.Children[4].TextContent.ShouldBe("Never");
        Row(cut, TestData.SamAgentId).Children[0].TextContent.ShouldNotContain("(you)");
    }

    [Fact]
    public void Roles_are_a_read_only_badge_with_the_identity_provider_note_and_never_an_input()
    {
        var cut = RenderPage();

        cut.Find("p.ts-roles-note").TextContent.ShouldBe("Roles come from your identity provider's groups.");
        cut.FindAll("td .ts-role").Select(r => (r.TextContent, r.ClassName)).ShouldBe(
            [("Admin", "ts-role ts-role--admin"), ("Agent", "ts-role ts-role--agent"), ("Agent", "ts-role ts-role--agent")]);
        cut.FindAll("select").ShouldBeEmpty();
        cut.FindAll("input").ShouldBeEmpty();
    }

    [Fact]
    public void The_first_page_asks_for_25()
    {
        RenderPage();

        _agents.Received(1).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_page_in_the_query_string_asks_for_that_page()
    {
        RenderPage("?page=3");

        _agents.Received(1).ListPageAsync(3, 25, Arg.Any<CancellationToken>());
        _agents.DidNotReceive().ListPageAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin)], 1, 25, 60)));
        var cut = RenderPage();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/settings/agents?page=2");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<AgentListItemDto>>("api-error", "The API is unavailable."),
            TestData.Ok(Page(TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin))));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the agents. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked_for_the_list()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _agents.DidNotReceive().ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- activate: no confirmation, once -------------------------------------------------------------------------

    [Fact]
    public void Activating_needs_no_confirmation_sends_one_write_with_no_cancellation_and_shows_the_agent_active()
    {
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().ShouldBe([(TestData.RaeAgentId, true)]);
        _agents.Received(1).SetActiveAsync(TestData.RaeAgentId, true, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Active");
        Row(cut, TestData.RaeAgentId).QuerySelectorAll("button.ts-activate").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Activated Rae Quinn");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void Two_clicks_on_activate_while_the_first_runs_send_one_write()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();
        cut.FindAll("button.ts-activate, button.ts-deactivate").ShouldAllBe(b => b.HasAttribute("disabled"));
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(1);
        gate.SetResult(TestData.Ok(new AgentDto(TestData.RaeAgentId, "Rae Quinn", "rae@example.com", AgentRoles.Agent, true, null, null)));
        cut.WaitForAssertion(() => Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Active"));
    }

    [Fact]
    public void A_failed_activation_says_nothing_changed_and_leaves_the_row_inactive()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("boom", "The API refused."));
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't activate Rae Quinn. Nothing was changed. The API refused.");
        Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Inactive");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_answer_on_activate_never_claims_nothing_changed_and_offers_a_reload()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The change to Rae Quinn may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");

        alert.QuerySelector("button")!.Click();

        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Writes().Count().ShouldBe(1);
    }

    // ---- deactivate: asks first, once ----------------------------------------------------------------------------

    [Fact]
    public void Deactivating_asks_first_names_the_agent_and_the_consequence_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        var dialog = DeactivateDialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Deactivate Sam Ortiz?");
        dialog.TextContent.ShouldContain("They will lose access to TechStrap straight away.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        dialog.TextContent.ShouldNotContain("This is your own account");
        Confirm(cut).TextContent.ShouldBe("Deactivate agent");
        Dialogs.VerifyInvoke("open", 1);
        Writes().ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPage();

        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Writes().ShouldBeEmpty();
        Dialogs.VerifyInvoke("close", 2);
        Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Active");
    }

    [Fact]
    public void Confirming_deactivates_that_agent_once_and_the_row_shows_inactive_with_its_activate_button()
    {
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        Writes().ShouldBe([(TestData.SamAgentId, false)]);
        _agents.Received(1).SetActiveAsync(TestData.SamAgentId, false, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Inactive");
        Row(cut, TestData.SamAgentId).QuerySelectorAll("button.ts-activate").Count.ShouldBe(1);
        StatusMessages.Current.ShouldBe("Deactivated Sam Ortiz");
        Dialogs.VerifyInvoke("close", 1);
        Session.State.ShouldBe(AgentSessionState.Ready, "someone else's deactivation does not touch my session");
    }

    [Fact]
    public void A_double_click_on_confirm_deactivates_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();
        DeactivateDialog(cut).QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        DeactivateDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Writes().Count().ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok(new AgentDto(TestData.SamAgentId, "Sam Ortiz", "sam@example.com", AgentRoles.Agent, false, null, null)));
        cut.WaitForAssertion(() => Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Inactive"));
    }

    [Fact]
    public void The_last_active_admin_conflict_is_shown_inside_the_dialog_in_the_apis_words_and_changes_nothing()
    {
        const string Message = "TechStrap needs at least one active admin. Make sure another admin is active before turning this one off.";
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.LastActiveAdmin, Message, ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, TestData.AdaAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        DeactivateDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe(Message);
        Row(cut, TestData.AdaAgentId).Children[3].TextContent.ShouldBe("Active");
        Dialogs.VerifyNotInvoke("close");
        StatusMessages.Current.ShouldBeNull();
        Session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public void Another_failure_says_nothing_was_changed_and_keeps_the_dialog_open()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("boom", "The API refused."));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        DeactivateDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't deactivate the agent. Nothing was changed. The API refused.");
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_write_and_offers_a_reload()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        Confirm(cut).Click();

        var dialog = DeactivateDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The change may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Writes().Count().ShouldBe(1);

        DeactivateDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void An_agent_who_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentNotFound, "No such agent.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That agent no longer exists.");
        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    // ---- an unknown outcome is held until the list has been read again --------------------------------------------

    [Fact]
    public void After_a_lost_activate_answer_a_second_click_sends_nothing_until_the_list_has_been_read_again()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The change to Rae Quinn may have gone through.");

        cut.Find(".ts-conflict button").Click();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(2);
    }

    [Fact]
    public void After_a_lost_deactivate_answer_asking_again_shows_the_uncertain_copy_and_sends_nothing()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        Confirm(cut).Click();
        DeactivateDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();

        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        DeactivateDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("The change may have gone through. Reload the list to check before you try again.");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Writes().Count().ShouldBe(1);
    }

    [Fact]
    public void A_read_that_started_before_the_lost_answer_does_not_release_the_hold_and_one_that_started_after_it_does()
    {
        var cut = RenderPage();
        var gate = new TaskCompletionSource<Result<PagedResponse<AgentListItemDto>>>();
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        _navigation.NavigateTo("/settings/agents?page=2");
        cut.WaitForAssertion(() => _agents.Received(1).ListPageAsync(2, 25, Arg.Any<CancellationToken>()));

        // The page-2 read is under way (page 1 is still on screen) when the answer to the write is lost.
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();
        Writes().Count().ShouldBe(1);

        // That read finishes. It started before the lost answer, so it may not have seen the write, and the hold stays.
        gate.SetResult(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false)], 2, 25, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-agent]").Count.ShouldBe(1));
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The change to Rae Quinn may have gone through.");

        // A read that starts now is after the write: it releases the hold.
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false)], 2, 25, 26)));
        cut.Find(".ts-conflict button").Click();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(2);
    }

    [Fact]
    public void An_agent_who_is_already_gone_when_activating_says_so_and_reloads_the_list()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentNotFound, "No such agent.", ResultErrorKind.NotFound));
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        StatusMessages.Current.ShouldBe("That agent no longer exists.");
        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        cut.FindAll(".ts-conflict[role=alert]").ShouldBeEmpty();
    }

    // ---- stale loads and pages past the end ----------------------------------------------------------------------

    [Fact]
    public void A_slow_answer_for_an_earlier_page_never_replaces_the_newest_load()
    {
        var older = new TaskCompletionSource<Result<PagedResponse<AgentListItemDto>>>();
        var newer = new TaskCompletionSource<Result<PagedResponse<AgentListItemDto>>>();
        _agents.ListPageAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(older.Task);
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(newer.Task);
        var cut = RenderPage();
        _navigation.NavigateTo("/settings/agents?page=2");
        cut.WaitForAssertion(() => _agents.Received(1).ListPageAsync(2, 25, Arg.Any<CancellationToken>()));

        newer.SetResult(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId)], 2, 25, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-agent]").Count.ShouldBe(1));
        older.SetResult(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Sam Ortiz", TestData.SamAgentId), TestData.AgentRow("Ada Admin", TestData.AdaAgentId)], 1, 25, 2)));
        cut.Render();

        cut.FindAll("tr[data-agent]").Select(r => r.GetAttribute("data-agent")).ShouldBe([TestData.RaeAgentId.ToString()]);
        cut.FindAll("[aria-busy=true]").ShouldBeEmpty();
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_page_with_rows_and_never_says_there_are_no_agents()
    {
        _agents.ListPageAsync(99, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([], 99, 25, 30)));
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId)], 2, 25, 30)));

        var cut = RenderPage("?page=99");

        cut.WaitForAssertion(() => cut.FindAll("tr[data-agent]").Count.ShouldBe(1));
        _navigation.Uri.ShouldEndWith("/settings/agents?page=2");
        cut.Markup.ShouldNotContain("No agents yet");
    }

    [Fact]
    public void A_page_past_the_end_of_a_two_page_list_goes_back_to_the_first_page_without_a_page_number()
    {
        _agents.ListPageAsync(3, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([], 3, 25, 10)));

        var cut = RenderPage("?page=3");

        cut.WaitForAssertion(() => cut.FindAll("tr[data-agent]").Count.ShouldBe(3));
        _navigation.Uri.ShouldEndWith("/settings/agents");
    }

    // ---- deactivating yourself -----------------------------------------------------------------------------------

    [Fact]
    public void Deactivating_yourself_warns_in_the_dialog_and_after_it_succeeds_the_session_asks_the_api_again()
    {
        var cut = RenderPage();
        Row(cut, TestData.AdaAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).TextContent.ShouldContain("This is your own account.");
        _agents.ClearReceivedCalls();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentInactive, "Your agent account is deactivated.", ResultErrorKind.Forbidden));

        Confirm(cut).Click();

        Writes().ShouldBe([(TestData.AdaAgentId, false)]);
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
        Session.State.ShouldBe(AgentSessionState.NoAccess);
    }

    // ---- no stale session flicker --------------------------------------------------------------------------------

    [Fact]
    public void The_page_never_makes_a_call_a_plain_agent_would_be_refused()
    {
        AsAgent();
        var cut = RenderPage();

        cut.Markup.ShouldNotContain("Deactivate");
        _agents.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBe([nameof(IAgentsClient.GetMeAsync)]);
    }
}
