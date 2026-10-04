using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The page with the sidebar and the composer together: state flows, the conflict banner, and what a reload keeps.</summary>
public sealed class TicketDetailConflictTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();

    public TicketDetailConflictTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        ShowTicket(TestData.Detail());
    }

    private void ShowTicket(TicketDetailDto detail) =>
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    private void ReplyReturns(params Result<AgentMessageResponse>[] results) =>
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(results[0], results[1..]);

    private static Result<AgentMessageResponse> Conflict() =>
        TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict);

    private void PriorityConflicts() =>
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));

    [Fact]
    public void An_open_ticket_has_the_sidebar_controls_in_the_side_panel_instead_of_the_read_only_facts()
    {
        var cut = RenderTicket();

        cut.Find("aside.ts-side section.ts-sidebar").ShouldNotBeNull();
        cut.FindAll("section.ts-facts").ShouldBeEmpty();
        cut.Find("aside.ts-side section.ts-requester").ShouldNotBeNull();
    }

    [Fact]
    public void A_sidebar_change_applies_the_returned_state_reloads_and_hands_the_composer_the_new_row_version()
    {
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.State(TicketStatuses.Solved, rowVersion: 8)));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal), TestData.State(rowVersion: 9))));
        var cut = RenderTicket();
        ShowTicket(TestData.Detail(status: TicketStatuses.Solved, rowVersion: 8));

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        cut.WaitForAssertion(() => _tickets.Received(2).GetAsync("ORB-42", Arg.Any<CancellationToken>()));
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Solved");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("After the status change.");
        cut.Find(".ts-composer-actions button").Click();

        _tickets.Received(1).AddNoteAsync(TestData.TicketId, Arg.Is<AddInternalNoteRequest>(r => r.RowVersion == 8u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_conflict_from_the_composer_shows_the_banner_and_reload_refreshes_while_the_draft_and_files_stay()
    {
        ReplyReturns(Conflict(), TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 10))));
        var cut = RenderTicket();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[4], "log.txt", null, "text/plain"));
        cut.Find("textarea").Input("A reply I must not lose.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var banner = cut.Find(".ts-conflict");
        banner.GetAttribute("role").ShouldBe("alert");
        banner.TextContent.ShouldContain("This ticket changed since you opened it");
        banner.TextContent.ShouldContain("Your reply draft is kept.");

        ShowTicket(TestData.Detail(rowVersion: 9, events: [TestData.Event(TicketEventTypes.PriorityChanged, """{"from":"Normal","to":"High"}""", actorName: "Ada Admin")]));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict--reloaded").TextContent.ShouldContain("Reloaded. Latest change: Priority changed from Normal to High (Ada Admin)."));
        cut.Find("textarea").GetAttribute("value").ShouldBe("A reply I must not lose.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _tickets.Received(1).ReplyAsync(
            TestData.TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 9u && r.Body == "A reply I must not lose."),
            Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_conflict_from_the_sidebar_shows_the_same_banner_leaves_the_display_alone_and_dismiss_hides_it()
    {
        PriorityConflicts();
        var cut = RenderTicket();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.Find(".ts-conflict").TextContent.ShouldContain("This ticket changed since you opened it");
        cut.Find("select[id^='ts-sidebar-priority-']").QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent.ShouldBe("Normal");

        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict--reloaded").Count.ShouldBe(1));
        cut.Find(".ts-conflict--reloaded").TextContent.ShouldNotContain("Latest change");

        cut.Find(".ts-conflict button").Click();

        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }

    [Fact]
    public void A_reload_that_fails_keeps_the_stale_banner_and_one_that_finds_the_ticket_gone_replaces_the_page()
    {
        PriorityConflicts();
        var cut = RenderTicket();
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket [role=alert] p").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll(".ts-conflict--reloaded").ShouldBeEmpty();
        cut.Find(".ts-conflict").TextContent.ShouldContain("This ticket changed since you opened it");

        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
    }

    [Fact]
    public void A_sidebar_change_on_a_ticket_deleted_meanwhile_ends_on_this_ticket_no_longer_exists_with_a_way_back()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
        cut.Find("section.ts-gone a").GetAttribute("href").ShouldBe("/queue");
    }

    [Fact]
    public void An_uncertain_sidebar_failure_offers_reload_which_refreshes_silently_and_keeps_the_draft()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderTicket();
        cut.Find("textarea").Input("Keep this.");

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);
        cut.Find(".ts-control-error").TextContent.ShouldContain("may already have been saved");

        ShowTicket(TestData.Detail(priority: TicketPriorities.Urgent, rowVersion: 9));
        cut.Find(".ts-control-reload").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict--reloaded").Count.ShouldBe(1));
        cut.FindAll(".ts-control-error").ShouldBeEmpty();
        cut.Find("select[id^='ts-sidebar-priority-']").QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent.ShouldBe("Urgent");
        cut.Find("textarea").GetAttribute("value").ShouldBe("Keep this.");
    }

    [Fact]
    public void A_composer_conflict_reload_keeps_the_composer_mounted_while_the_refresh_is_pending()
    {
        ReplyReturns(Conflict());
        var cut = RenderTicket();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[4], "log.txt", null, "text/plain"));
        cut.Find("textarea").Input("Still here.");
        cut.FindAll(".ts-composer-actions button")[0].Click();

        var gate = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(gate.Task);
        cut.Find(".ts-conflict button").Click();

        cut.Find("textarea").GetAttribute("value").ShouldBe("Still here.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");
        cut.Find(".ts-conflict button").HasAttribute("disabled").ShouldBeTrue();

        gate.SetResult(TestData.Ok(TestData.Detail(rowVersion: 9)));

        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict--reloaded").Count.ShouldBe(1));
        cut.Find("textarea").GetAttribute("value").ShouldBe("Still here.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");
    }

    [Fact]
    public void The_conflict_banner_takes_focus_on_its_reload_button_when_it_appears()
    {
        PriorityConflicts();
        var cut = RenderTicket();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        var id = cut.Find(".ts-conflict button").GetAttribute("blazor:elementReference");
        cut.WaitForAssertion(() => ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id.ShouldBe(id));
    }

    [Fact]
    public void A_ticket_closed_refusal_refreshes_the_page_so_the_sidebar_gives_way_to_the_closed_facts()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketClosed, "This ticket is closed.", ResultErrorKind.Conflict));
        var cut = RenderTicket();
        ShowTicket(TestData.Detail(status: TicketStatuses.Closed, rowVersion: 9));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.WaitForAssertion(() => cut.FindAll("section.ts-sidebar").ShouldBeEmpty());
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Closed");
    }

    [Fact]
    public void A_sidebar_write_after_a_reply_uses_the_version_the_reply_returned_not_a_stale_one()
    {
        ReplyReturns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 8))));
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.State(priority: TicketPriorities.Urgent, rowVersion: 10)));
        var cut = RenderTicket();
        cut.Find("textarea").Input("Hello.");
        ShowTicket(TestData.Detail(rowVersion: 9));

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.WaitForAssertion(() => _tickets.Received(2).GetAsync("ORB-42", Arg.Any<CancellationToken>()));
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        _tickets.Received(1).ChangePriorityAsync(TestData.TicketId, Arg.Is<ChangeTicketPriorityRequest>(r => r.RowVersion == 9u), Arg.Any<CancellationToken>());
    }
}
