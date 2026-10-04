using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 4: a plain agent never sees or triggers delete or erase, nothing destructive fires without the typed confirmation, and Enter never confirms a dialog.</summary>
public sealed class DestructiveActionTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IRequestersClient _requesters = Substitute.For<IRequestersClient>();
    private readonly NavigationManager _navigation;
    private readonly List<TicketStateDto> _states = [];
    private int _conflicts;
    private int _gone;
    private bool _admin;

    public DestructiveActionTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_requesters);
        // Built on first use, after RenderActions has said whether this test signs in an Agent or an Admin (services cannot be added once something has been resolved).
        Services.AddSingleton(_ => AgentSessions.SignedIn(_admin));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: true, rowVersion: 8)));
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        _navigation = Services.GetRequiredService<NavigationManager>();
        _navigation.NavigateTo("/tickets/ORB-42");
    }

    private IRenderedComponent<TicketActions> RenderActions(bool admin = false, TicketDetailViewModel? ticket = null)
    {
        _admin = admin;
        return Render<TicketActions>(p => p
            .Add(c => c.Ticket, ticket ?? TestData.Model())
            .Add(c => c.OnState, state => _states.Add(state))
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++));
    }

    private static IReadOnlyList<string> MenuItems(IRenderedComponent<TicketActions> cut) =>
        cut.FindAll("ul.ts-menu button").Select(b => Regex.Replace(b.TextContent, @"\s+", " ").Trim()).ToList();

    private static void OpenDialog(IRenderedComponent<TicketActions> cut, string menuItem) =>
        cut.FindAll("ul.ts-menu button").Single(b => b.TextContent.Contains(menuItem)).Click();

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<TicketActions> cut, string titleContains) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.Contains(titleContains));

    private static void Type(AngleSharp.Dom.IElement dialog, string text) => dialog.QuerySelector("input")!.Input(text);

    private static AngleSharp.Dom.IElement Confirm(AngleSharp.Dom.IElement dialog) =>
        dialog.QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    [Fact]
    public void A_plain_agent_gets_spam_and_nothing_destructive_not_even_in_hidden_markup()
    {
        var cut = RenderActions(admin: false);

        MenuItems(cut).ShouldBe(["Mark as spam"]);
        cut.Markup.ShouldNotContain("Delete");
        cut.Markup.ShouldNotContain("Erase");
        cut.FindAll("dialog").Count.ShouldBe(1);
        cut.FindAll("dialog input").ShouldBeEmpty();
    }

    [Fact]
    public void An_admin_gets_spam_delete_and_erase_with_the_danger_entries_marked_by_a_word_and_a_class()
    {
        var cut = RenderActions(admin: true);

        MenuItems(cut).ShouldBe(["Mark as spam", "Delete ticket (permanent)", "Erase requester (permanent)"]);
        cut.FindAll("button.ts-menu-item--danger").Count.ShouldBe(2);
        cut.FindAll("dialog").Count.ShouldBe(3);
    }

    [Fact]
    public void The_menu_is_closed_until_opened_and_reports_its_state()
    {
        var cut = RenderActions();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("false");

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("true");
    }

    [Fact]
    public void Marking_spam_asks_first_naming_the_ticket_and_does_nothing_until_confirmed()
    {
        var cut = RenderActions();

        OpenDialog(cut, "Mark as spam");

        var dialog = Dialog(cut, "Mark ORB-42 as spam?");
        dialog.TextContent.ShouldContain("This hides ORB-42 from the normal queue views and flags it as spam. You can restore it from the Spam view.");
        dialog.QuerySelector("input").ShouldBeNull();
        Dialogs.VerifyInvoke("open", 1);
        _tickets.ReceivedCalls().ShouldBeEmpty();

        Confirm(dialog).Click();

        _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == true && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Marked ORB-42 as spam");
    }

    [Fact]
    public void Cancelling_or_pressing_escape_in_any_dialog_makes_no_call()
    {
        var cut = RenderActions(admin: true);

        OpenDialog(cut, "Mark as spam");
        Dialog(cut, "Mark ORB-42").QuerySelector("button.btn-outline-secondary")!.Click();
        OpenDialog(cut, "Delete ticket");
        Dialog(cut, "Delete ORB-42").TriggerEvent("oncancel", EventArgs.Empty);
        OpenDialog(cut, "Erase requester");
        Dialog(cut, "Erase this requester").QuerySelector("button.btn-outline-secondary")!.Click();

        _tickets.ReceivedCalls().ShouldBeEmpty();
        _requesters.ReceivedCalls().ShouldBeEmpty();
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyInvoke("close", 3);
    }

    [Fact]
    public void Deleting_needs_the_ticket_number_typed_and_only_then_calls_delete_once_and_goes_to_the_queue()
    {
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        var dialog = Dialog(cut, "Delete ORB-42 permanently?");

        dialog.TextContent.ShouldContain("This permanently removes the ticket with all of its messages and attachments.");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type ORB-42 to confirm");
        Confirm(dialog).HasAttribute("disabled").ShouldBeTrue();
        Confirm(dialog).Click();
        _tickets.ReceivedCalls().ShouldBeEmpty();

        Type(dialog, "ORB-4");
        Confirm(Dialog(cut, "Delete ORB-42")).HasAttribute("disabled").ShouldBeTrue();
        Type(Dialog(cut, "Delete ORB-42"), "orb-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        _tickets.Received(1).DeleteAsync(TestData.TicketId, Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Deleted ORB-42");
    }

    [Fact]
    public void Erasing_needs_the_requester_email_typed_names_every_ticket_and_shows_no_counts()
    {
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Erase requester");
        var dialog = Dialog(cut, "Erase this requester?");

        dialog.TextContent.ShouldContain("ada@example.com");
        dialog.TextContent.ShouldContain("every ticket from this requester");
        Regex.IsMatch(dialog.TextContent, @"\d+\s+(tickets?|attachments?|messages?)").ShouldBeFalse("the API has no counts to show");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type ada@example.com to confirm");
        Confirm(dialog).HasAttribute("disabled").ShouldBeTrue();

        Type(dialog, "ADA@example.com");
        Confirm(Dialog(cut, "Erase this requester")).Click();

        _requesters.Received(1).EraseAsync(TestData.RequesterId, Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Erased the requester of ORB-42");
        StatusMessages.Current!.ShouldNotContain("ada@example.com");
    }

    [Fact]
    public void Enter_cannot_confirm_a_destructive_dialog_it_has_no_form_and_no_submit_button()
    {
        var cut = RenderActions(admin: true);

        foreach (var dialog in cut.FindAll("dialog"))
        {
            dialog.QuerySelectorAll("form").ShouldBeEmpty();
            dialog.QuerySelectorAll("button").ShouldAllBe(b => b.GetAttribute("type") == "button");
        }

        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_double_click_on_confirm_deletes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");

        Confirm(Dialog(cut, "Delete ORB-42")).Click();
        var dialog = Dialog(cut, "Delete ORB-42");
        dialog.QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        dialog.QuerySelector("input")!.HasAttribute("disabled").ShouldBeTrue();
        dialog.TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(dialog).Click();

        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.DeleteAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => _navigation.Uri.ShouldEndWith("/queue"));
    }

    [Fact]
    public void A_failed_delete_keeps_the_dialog_open_shows_why_and_changes_nothing()
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API is unavailable."));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");

        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        Dialog(cut, "Delete ORB-42").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't delete the ticket. Nothing was changed. The API is unavailable.");
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        StatusMessages.Current.ShouldBeNull();
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_failed_erase_keeps_the_dialog_open_and_a_ticket_deleted_meanwhile_is_handed_to_the_page()
    {
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("requester-not-found", "No such requester.", ResultErrorKind.NotFound));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Erase requester");
        Type(Dialog(cut, "Erase this requester"), "ada@example.com");

        Confirm(Dialog(cut, "Erase this requester")).Click();

        Dialog(cut, "Erase this requester").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't erase the requester. Nothing was changed. No such requester.");
        _gone.ShouldBe(0);

        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        _gone.ShouldBe(1);
    }

    [Fact]
    public void A_spam_conflict_closes_the_dialog_and_raises_the_conflict_without_navigating()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderActions();
        OpenDialog(cut, "Mark as spam");

        Confirm(Dialog(cut, "Mark ORB-42")).Click();

        _conflicts.ShouldBe(1);
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void Not_spam_is_offered_to_an_agent_on_a_flagged_ticket_needs_no_dialog_and_stays_on_the_ticket()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: false, rowVersion: 8)));
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        MenuItems(cut).ShouldBe(["Not spam u"]);
        cut.FindAll("dialog").ShouldBeEmpty();
        cut.Find("ul.ts-menu button").Click();

        _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _states.ShouldHaveSingleItem().IsSpam.ShouldBeFalse();
        StatusMessages.Current.ShouldBe("Restored ORB-42 from spam");
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void A_failed_not_spam_says_so_in_the_status_bar_and_changes_nothing()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketStateDto>("boom", "The API is unavailable."));
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        cut.Find("ul.ts-menu button").Click();

        StatusMessages.Current.ShouldBe("Couldn't restore the ticket from spam. The API is unavailable.");
        _states.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_restores_a_flagged_ticket_once_and_is_ignored_in_every_other_case()
    {
        RenderActions(admin: false, TestData.Model(isSpam: true));

        await PressAsync("u", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("u");
        ShortcutService.SingleKeyEnabled = true;
        _tickets.ReceivedCalls().ShouldBeEmpty();

        await PressAsync("u");

        await _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false), Arg.Any<CancellationToken>());
        _states.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, TicketStatuses.Open)]
    [InlineData(true, TicketStatuses.Closed)]
    public async Task The_u_key_does_nothing_on_a_ticket_that_is_not_flagged_or_is_closed(bool isSpam, string status)
    {
        RenderActions(admin: false, TestData.Model(status, isSpam: isSpam));

        await PressAsync("u");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void A_closed_ticket_offers_an_agent_nothing_and_an_admin_only_delete_and_erase(bool admin, int expectedEntries)
    {
        var cut = RenderActions(admin, TestData.Model(TicketStatuses.Closed));

        cut.FindAll("ul.ts-menu button").Count.ShouldBe(expectedEntries);
        if (!admin)
        {
            cut.Markup.Trim().ShouldBeEmpty();
        }
        else
        {
            MenuItems(cut).ShouldBe(["Delete ticket (permanent)", "Erase requester (permanent)"]);
        }
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiTimeout)]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    [InlineData(ApiErrorCodes.UnexpectedResponse)]
    [InlineData(ApiErrorCodes.ApiError)]
    public void An_uncertain_delete_says_it_may_have_happened_and_offers_reload_and_the_queue_never_try_again(string code)
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(code, "Slow."));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");

        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        var dialog = Dialog(cut, "Delete ORB-42");
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe(ActionsCopy.DeleteUncertain);
        dialog.TextContent.ShouldNotContain("Try again");
        dialog.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ShouldContain(ActionsCopy.Reload);
        dialog.QuerySelector("a")!.GetAttribute("href").ShouldBe("/queue");
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
    }

    [Fact]
    public void Uncertain_spam_and_erase_say_so_too_and_the_erase_copy_carries_no_email()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ApiTimeout, "Slow."));
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiUnavailable, "Down."));
        var cut = RenderActions(admin: true);

        OpenDialog(cut, "Mark as spam");
        Confirm(Dialog(cut, "Mark ORB-42")).Click();
        Dialog(cut, "Mark ORB-42").QuerySelector("[role=alert]")!.TextContent.ShouldBe(ActionsCopy.SpamUncertain);
        Dialog(cut, "Mark ORB-42").QuerySelector("button.btn-outline-secondary")!.Click();

        OpenDialog(cut, "Erase requester");
        Type(Dialog(cut, "Erase this requester"), "ada@example.com");
        Confirm(Dialog(cut, "Erase this requester")).Click();
        var alert = Dialog(cut, "Erase this requester").QuerySelector("[role=alert]")!.TextContent;
        alert.ShouldBe(ActionsCopy.EraseUncertain);
        alert.ShouldNotContain("ada@example.com");
    }

    [Fact]
    public void The_reload_button_of_an_uncertain_write_asks_the_page_to_reload()
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "Slow."));
        var reloads = 0;
        _admin = true;
        var cut = Render<TicketActions>(p => p
            .Add(c => c.Ticket, TestData.Model())
            .Add(c => c.OnReload, () => reloads++));
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        Dialog(cut, "Delete ORB-42").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == ActionsCopy.Reload).Click();

        reloads.ShouldBe(1);
    }

    [Fact]
    public void An_uncertain_not_spam_says_it_may_have_happened_and_offers_reload_and_the_queue()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ApiTimeout, "Slow."));
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        cut.Find("ul.ts-menu button").Click();

        cut.Find(".ts-actions-uncertain[role=alert]").TextContent.ShouldContain(ActionsCopy.RestoreUncertain);
        cut.Find(".ts-actions-uncertain a").GetAttribute("href").ShouldBe("/queue");
        cut.Find(".ts-actions-uncertain button").TextContent.ShouldBe(ActionsCopy.Reload);
        StatusMessages.Current.ShouldBeNull();
        _states.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disposing_the_component_mid_request_never_cancels_the_write()
    {
        var gate = new TaskCompletionSource<Result>();
        CancellationToken seen = default;
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            seen = call.Arg<CancellationToken>();
            return gate.Task;
        });
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        await cut.InvokeAsync(() => cut.Instance.Dispose());

        seen.CanBeCanceled.ShouldBeFalse();
        gate.SetResult(TestData.Ok());
    }

    [Fact]
    public void A_second_menu_restore_while_the_first_runs_is_ignored_by_the_runner_itself()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        cut.Find("ul.ts-menu button").Click();
        cut.Find("ul.ts-menu button").Click();

        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.SetSpamAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok(TestData.State(isSpam: false)));
    }

    [Theory]
    [InlineData(ApiErrorCodes.Forbidden)]
    [InlineData(ApiErrorCodes.AdminAccessRequired)]
    public void A_403_from_delete_or_erase_is_a_plain_failure_not_an_uncertain_one(string code)
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(code, "Admins only.", ResultErrorKind.Forbidden));
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(code, "Admins only.", ResultErrorKind.Forbidden));
        var cut = RenderActions(admin: true);

        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();
        Dialog(cut, "Delete ORB-42").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't delete the ticket. Nothing was changed. Admins only.");
        Dialog(cut, "Delete ORB-42").QuerySelector("button.btn-outline-secondary")!.Click();

        OpenDialog(cut, "Erase requester");
        Type(Dialog(cut, "Erase this requester"), "ada@example.com");
        Confirm(Dialog(cut, "Erase this requester")).Click();
        Dialog(cut, "Erase this requester").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't erase the requester. Nothing was changed. Admins only.");

        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        cut.Markup.ShouldNotContain("may already");
    }

    [Fact]
    public void After_an_uncertain_failure_confirm_stays_disabled_so_only_reload_or_the_queue_remain()
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "Slow."));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        Confirm(Dialog(cut, "Delete ORB-42")).HasAttribute("disabled").ShouldBeTrue();
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.DeleteAsync)).ShouldBe(1);
        Dialog(cut, "Delete ORB-42").QuerySelector("button.btn-outline-secondary")!.HasAttribute("disabled").ShouldBeFalse();
    }
}
