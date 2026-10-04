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

public sealed class TicketSidebarTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly List<TicketStateDto> _states = [];
    private int _conflicts;
    private int _gone;
    private int _reloads;

    public TicketSidebarTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(AgentSessions.SignedIn());
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(TicketStatuses.Solved)));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(assigneeId: TestData.AdaAgentId)));
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(priority: TicketPriorities.Urgent)));
        _tickets.MoveProductAsync(Arg.Any<Guid>(), Arg.Any<MoveTicketProductRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(productId: TestData.NimbusId)));
        _tickets.AddTagAsync(Arg.Any<Guid>(), Arg.Any<AddTicketTagRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(tagIds: [TestData.BugTagId])));
        _tickets.RemoveTagAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State()));
    }

    private IRenderedComponent<TicketSidebar> RenderSidebar(TicketDetailViewModel? ticket = null) =>
        Render<TicketSidebar>(p => p
            .Add(c => c.Ticket, ticket ?? TestData.Model())
            .Add(c => c.OnState, state => _states.Add(state))
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++)
            .Add(c => c.OnReload, () => _reloads++));

    private static string Selected(IRenderedComponent<TicketSidebar> cut, string selector) =>
        cut.Find(selector).QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent;

    [Fact]
    public void The_controls_show_the_current_values_and_the_tags()
    {
        var cut = RenderSidebar(TestData.Model(TicketStatuses.Pending, TicketPriorities.High, TestData.SamAgentId, "Sam Ortiz",
            [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("Pending");
        Selected(cut, "select[id^='ts-sidebar-assignee-']").ShouldBe("Sam Ortiz");
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("High");
        Selected(cut, "select[id^='ts-sidebar-product-']").ShouldBe("Orbitly");
        cut.Find(".ts-tagpicker-list .ts-tag").TextContent.ShouldBe("bug");
        cut.FindAll("label").Select(l => l.TextContent).ShouldBe(["Status", "Assignee", "Priority", "Product", SidebarCopy.AddTagPlaceholder]);
    }

    [Fact]
    public void Status_calls_the_status_method_with_the_current_row_version_and_reports_the_returned_state()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        _tickets.Received(1).ChangeStatusAsync(TestData.TicketId, Arg.Is<ChangeTicketStatusRequest>(r => r.Status == "Solved" && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _states.ShouldHaveSingleItem().Status.ShouldBe("Solved");
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Assignee_calls_the_assign_method_and_choosing_Unassigned_sends_a_null_assignee()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-assignee-']").Change(TestData.AdaAgentId.ToString());
        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == TestData.AdaAgentId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        var assigned = RenderSidebar(TestData.Model(assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz", rowVersion: 9));
        assigned.Find("select[id^='ts-sidebar-assignee-']").Change(string.Empty);

        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == null && r.RowVersion == 9u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Priority_and_product_call_their_methods_with_the_current_row_version()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);
        _tickets.Received(1).ChangePriorityAsync(TestData.TicketId, Arg.Is<ChangeTicketPriorityRequest>(r => r.Priority == "Urgent" && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        cut.Find("select[id^='ts-sidebar-product-']").Change(TestData.NimbusId.ToString());
        _tickets.Received(1).MoveProductAsync(TestData.TicketId, Arg.Is<MoveTicketProductRequest>(r => r.ProductId == TestData.NimbusId && r.RowVersion == 7u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Adding_a_tag_sends_the_tag_and_the_row_version_and_removing_one_sends_the_row_version_for_the_query()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7, tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        cut.Find(".ts-tagpicker select").Change(TestData.BillingTagId.ToString());
        _tickets.Received(1).AddTagAsync(TestData.TicketId, Arg.Is<AddTicketTagRequest>(r => r.TagId == TestData.BillingTagId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        cut.Find("button[aria-label='Remove tag bug']").Click();
        _tickets.Received(1).RemoveTagAsync(TestData.TicketId, TestData.BugTagId, 7u, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_tag_picker_offers_only_tags_the_ticket_does_not_have_and_puts_the_placeholder_back_after_an_add()
    {
        var cut = RenderSidebar(TestData.Model(tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        cut.FindAll(".ts-tagpicker select option").Select(o => o.TextContent).ShouldBe([SidebarCopy.AddTagPlaceholder, "billing"]);

        cut.Find(".ts-tagpicker select").Change(TestData.BillingTagId.ToString());

        cut.Find(".ts-tagpicker select").QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent.ShouldBe(SidebarCopy.AddTagPlaceholder);
    }

    [Fact]
    public void A_ticket_with_no_tags_says_so_and_a_ticket_with_every_tag_offers_no_add_select()
    {
        RenderSidebar().Find(".ts-tagpicker-none").TextContent.ShouldBe("No tags");

        var all = RenderSidebar(TestData.Model(tags:
            [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626"), new TicketTagDto(TestData.BillingTagId, "billing", "#1D4ED8")]));

        all.FindAll(".ts-tagpicker select").ShouldBeEmpty();
    }

    [Fact]
    public void While_a_change_is_saving_every_control_is_disabled_the_field_says_saving_and_a_second_change_is_ignored()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        cut.Find("section.ts-sidebar").GetAttribute("aria-busy").ShouldBe("true");
        cut.FindAll("select, button").ShouldAllBe(e => e.HasAttribute("disabled"));
        cut.Find(".ts-saving").TextContent.ShouldBe(SidebarCopy.Saving);
        cut.Find(".ts-saving").GetAttribute("role").ShouldBe("status");

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);
        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ChangePriorityAsync)).ShouldBe(0);

        gate.SetResult(TestData.Ok(TestData.State(TicketStatuses.Solved)));
        cut.WaitForAssertion(() => cut.FindAll("select").ShouldAllBe(e => !e.HasAttribute("disabled")));
        cut.FindAll(".ts-saving").ShouldBeEmpty();
        _states.Count.ShouldBe(1);
    }

    [Fact]
    public void A_refused_change_shows_the_API_s_message_under_the_control_and_the_previous_value_comes_back()
    {
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.InvalidStatusTransition, "A Pending ticket can't go straight to New.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Closed);

        cut.Find(".ts-control-error").TextContent.ShouldBe("A Pending ticket can't go straight to New.");
        cut.Find(".ts-control-error").GetAttribute("role").ShouldBe("alert");
        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("Open");
        _states.ShouldBeEmpty();
        _conflicts.ShouldBe(0);
    }

    [Fact]
    public void A_failure_on_one_control_leaves_the_others_untouched_and_the_error_clears_on_the_next_try()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<TicketStateDto>("boom", "The API is unavailable."), TestData.Ok(TestData.State(priority: TicketPriorities.High)));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);
        cut.FindAll(".ts-control-error").Single().TextContent.ShouldBe("The API is unavailable.");
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("Normal");

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        cut.FindAll(".ts-control-error").ShouldBeEmpty();
        _states.Count.ShouldBe(1);
    }

    [Fact]
    public void A_stale_row_version_raises_the_conflict_changes_nothing_on_screen_and_shows_no_inline_error()
    {
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-assignee-']").Change(TestData.SamAgentId.ToString());

        _conflicts.ShouldBe(1);
        _states.ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        Selected(cut, "select[id^='ts-sidebar-assignee-']").ShouldBe("Unassigned");
        cut.FindAll("select").ShouldAllBe(e => !e.HasAttribute("disabled"));
    }

    [Fact]
    public void A_ticket_deleted_meanwhile_raises_gone()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        _gone.ShouldBe(1);
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Every_write_uses_the_row_version_the_page_holds_now_not_the_one_it_rendered_with()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.Render(p => p.Add(c => c.Ticket, TestData.Model(priority: TicketPriorities.Urgent, rowVersion: 12)));
        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        _tickets.Received(1).ChangePriorityAsync(Arg.Any<Guid>(), Arg.Is<ChangeTicketPriorityRequest>(r => r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _tickets.Received(1).ChangeStatusAsync(Arg.Any<Guid>(), Arg.Is<ChangeTicketStatusRequest>(r => r.RowVersion == 12u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Choosing_the_value_that_is_already_set_makes_no_call()
    {
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Open);
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Normal);
        cut.Find("select[id^='ts-sidebar-assignee-']").Change(string.Empty);
        cut.Find("select[id^='ts-sidebar-product-']").Change(TestData.OrbitlyId.ToString());

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Assign_to_me_is_offered_until_the_ticket_is_mine_and_assigns_the_signed_in_agent()
    {
        var cut = RenderSidebar();

        cut.Find("button.btn-outline-secondary").TextContent.ShouldBe("Assign to me");
        cut.Find("button.btn-outline-secondary").Click();

        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == AgentSessions.SamId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        var mine = RenderSidebar(TestData.Model(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz"));
        mine.FindAll("button").Where(b => b.TextContent == "Assign to me").ShouldBeEmpty();
    }

    [Fact]
    public void A_status_the_list_does_not_offer_still_shows_as_the_current_choice()
    {
        var cut = RenderSidebar(TestData.Model(TicketStatuses.New));

        cut.Find("select[id^='ts-sidebar-status-']").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["New", "Open", "Pending", "Solved", "Closed"]);
        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("New");
    }

    [Fact]
    public void A_current_product_that_is_no_longer_in_the_active_list_stays_listed_and_selected()
    {
        var model = TestData.Model() with { ProductId = Guid.NewGuid(), ProductName = "Legacy" };

        var cut = RenderSidebar(model);

        Selected(cut, "select[id^='ts-sidebar-product-']").ShouldBe("Legacy");
    }

    [Fact]
    public async Task The_e_key_focuses_the_assignee_control()
    {
        var cut = RenderSidebar();
        var assigneeId = cut.Find("select[id^='ts-sidebar-assignee-']").GetAttribute("blazor:elementReference");

        await PressAsync("e");

        ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id.ShouldBe(assigneeId);
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiTimeout)]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    [InlineData(ApiErrorCodes.UnexpectedResponse)]
    [InlineData(ApiErrorCodes.ApiError)]
    public void An_uncertain_failure_says_the_change_may_have_been_saved_and_offers_reload_not_try_again(string code)
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(code, "The API is unavailable."));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        cut.Find(".ts-control-error").TextContent.ShouldContain("may already have been saved");
        cut.Find(".ts-control-error").TextContent.ShouldContain("Reload");
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("Normal");
        cut.FindAll(".ts-control-error").ShouldNotContain(e => e.TextContent.Contains("Try again"));

        cut.Find(".ts-control-reload").Click();

        _reloads.ShouldBe(1);
        cut.FindAll(".ts-control-error").ShouldBeEmpty();
        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ChangePriorityAsync)).ShouldBe(1);
    }

    [Fact]
    public void A_definite_refusal_offers_no_reload_button()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketClosed, "This ticket is closed.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        cut.Find(".ts-control-error").TextContent.ShouldBe("This ticket is closed.");
        cut.FindAll(".ts-control-reload").ShouldBeEmpty();
    }

    [Fact]
    public void A_write_is_not_cancelled_when_the_sidebar_is_disposed_mid_request()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderSidebar();
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.Dispose();

        var seen = (CancellationToken)_tickets.ReceivedCalls().Single().GetArguments().Last()!;
        seen.IsCancellationRequested.ShouldBeFalse();
        gate.SetResult(TestData.Ok(TestData.State(priority: TicketPriorities.Urgent)));
    }

    [Fact]
    public void A_refusal_and_a_conflict_both_redraw_the_select_so_the_previous_value_is_visible_again()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<TicketStateDto>("boom", "Refused.", ResultErrorKind.Conflict),
            TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();
        // The select carries its @key revision. Bumping it makes Blazor replace the element, which puts the old value back in a real browser
        // (a select whose bound value did not change is otherwise left showing what the user picked).
        const string Select = "select[id^='ts-sidebar-priority-']";
        var before = cut.Find(Select).GetAttribute("data-revision");
        cut.Find(Select).Change(TicketPriorities.High);
        var afterRefusal = cut.Find(Select).GetAttribute("data-revision");
        afterRefusal.ShouldNotBe(before);

        cut.Find(Select).Change(TicketPriorities.Urgent);
        cut.Find(Select).GetAttribute("data-revision").ShouldNotBe(afterRefusal);
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("Normal");
    }

    [Theory]
    [InlineData("agent-not-found")]
    [InlineData("tag-not-found")]
    [InlineData("product-not-found")]
    public void A_404_for_something_other_than_the_ticket_shows_inline_with_reload_and_is_not_gone(string code)
    {
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(code, "That agent no longer exists.", ResultErrorKind.NotFound));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-assignee-']").Change(TestData.SamAgentId.ToString());

        _gone.ShouldBe(0);
        cut.Find(".ts-control-error").TextContent.ShouldBe("That agent no longer exists.");
        cut.Find(".ts-control-reload").TextContent.ShouldBe(SidebarCopy.ConflictReload);
    }

    [Fact]
    public void The_reload_button_is_outside_the_alert()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ApiTimeout, "Slow."));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        cut.FindAll("[role=alert] button").ShouldBeEmpty();
        cut.Find(".ts-control-reload").ShouldNotBeNull();
    }

    [Fact]
    public void A_ticket_closed_refusal_shows_inline_and_asks_the_page_to_refresh_so_it_reflects_Closed()
    {
        var closed = 0;
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketClosed, "This ticket is closed.", ResultErrorKind.Conflict));
        var cut = Render<TicketSidebar>(p => p.Add(c => c.Ticket, TestData.Model()).Add(c => c.OnClosed, () => closed++));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        closed.ShouldBe(1);
        cut.Find(".ts-control-error").TextContent.ShouldBe("This ticket is closed.");
    }
}
