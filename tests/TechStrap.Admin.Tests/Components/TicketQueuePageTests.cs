using Bunit;
using SyntaxCircus.Common;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketQueuePageTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();
    private readonly BunitJSModuleInterop _queueModule;
    private readonly NavigationManager _navigation;

    public TicketQueuePageTests()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([TestData.Summary("ORB-1"), TestData.Summary("ORB-2", "Billing question")])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts()));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_products);
        Services.AddSingleton(_tags);
        _queueModule = JSInterop.SetupModule("./js/queue.js");
        _queueModule.SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string? view = null, string query = "")
    {
        _navigation.NavigateTo($"/queue/{view}{query}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private IEnumerable<ListTicketsRequest> Requests() =>
        _tickets.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync))
            .Select(call => (ListTicketsRequest)call.GetArguments()[0]!);

    [Fact]
    public void The_root_route_shows_the_Unassigned_view_first_page_of_25()
    {
        _navigation.NavigateTo("/");
        Render<TicketQueuePage>();

        var request = Requests().Single();
        request.View.ShouldBe(TicketViews.Unassigned);
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.Search.ShouldBeNull();
    }

    [Theory]
    [InlineData("unassigned", TicketViews.Unassigned)]
    [InlineData("mine", TicketViews.Mine)]
    [InlineData("open", TicketViews.Open)]
    [InlineData("pending", TicketViews.Pending)]
    [InlineData("all", TicketViews.All)]
    [InlineData("spam", TicketViews.Spam)]
    public void Each_view_asks_the_API_for_exactly_that_view_and_only_the_Spam_tab_asks_for_spam(string slug, string expected)
    {
        RenderQueue(slug);

        Requests().Select(r => r.View).ShouldBe([expected]);
        if (expected != TicketViews.Spam)
        {
            Requests().ShouldNotContain(r => r.View == TicketViews.Spam);
        }
    }

    [Fact]
    public void The_view_in_the_route_is_case_insensitive()
    {
        RenderQueue("MINE");

        Requests().Single().View.ShouldBe(TicketViews.Mine);
    }

    [Fact]
    public void An_unknown_view_is_reported_as_not_found_and_calls_nothing()
    {
        var notFound = 0;
        _navigation.OnNotFound += (_, _) => notFound++;

        RenderQueue("everything");

        notFound.ShouldBe(1);
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public void The_six_tabs_link_to_their_views_and_show_counts_with_Spam_muted()
    {
        var cut = RenderQueue("open");

        var tabs = cut.FindAll("nav.ts-tabs a");
        tabs.Select(t => t.GetAttribute("href")).ShouldBe(
            ["/queue/unassigned", "/queue/mine", "/queue/open", "/queue/pending", "/queue/all", "/queue/spam"]);
        tabs.Select(t => t.QuerySelector(".ts-count")!.TextContent).ShouldBe(["3", "2", "5", "1", "11", "4"]);
        cut.FindAll(".ts-count--muted").Count.ShouldBe(1);
        cut.Find(".ts-tab--spam .ts-count").ClassList.ShouldContain("ts-count--muted");
        cut.Find("a[aria-current=page]").TextContent.ShouldContain("Open");
    }

    [Fact]
    public void A_failed_counts_call_leaves_the_tabs_without_numbers_and_the_list_working()
    {
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketViewCountsResponse>("boom"));

        var cut = RenderQueue("open");

        cut.FindAll(".ts-count").ShouldBeEmpty();
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public void Tab_links_keep_the_filters_but_not_the_page()
    {
        var cut = RenderQueue("open", "?status=Pending&page=3");

        cut.FindAll("nav.ts-tabs a")[1].GetAttribute("href").ShouldBe("/queue/mine?status=Pending");
    }

    [Fact]
    public void A_row_shows_number_subject_tags_product_requester_stamp_priority_assignee_and_age()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page(
        [
            TestData.Summary("ORB-42", "Cannot log in", TicketStatuses.Pending, TicketPriorities.Urgent, assignee: "Sam Ortiz",
                tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]),
        ])));

        var cut = RenderQueue("open");

        var row = cut.Find("tbody tr");
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe("/tickets/ORB-42");
        row.QuerySelector(".ts-subject")!.TextContent.ShouldBe("Cannot log in");
        row.QuerySelector(".ts-tag")!.TextContent.ShouldBe("bug");
        row.QuerySelector(".ts-product")!.TextContent.ShouldBe("Orbitly");
        row.QuerySelector(".ts-col-requester")!.TextContent.ShouldBe("Ada Lovelace");
        row.QuerySelector(".ts-stamp")!.TextContent.ShouldBe("Pending");
        row.QuerySelector(".ts-stamp")!.ClassList.ShouldContain("ts-stamp--queue");
        row.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        row.QuerySelector(".ts-avatar")!.TextContent.ShouldContain("SO");
        row.QuerySelector("time")!.TextContent.ShouldBe("5 min ago");
    }

    [Fact]
    public void A_row_without_an_assignee_or_requester_name_falls_back_and_a_spam_row_wears_the_spam_stamp()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page(
            [TestData.Summary("ORB-7", requesterName: null, isSpam: true)])));

        var cut = RenderQueue("spam");

        var row = cut.Find("tbody tr");
        row.QuerySelector(".ts-col-requester")!.TextContent.ShouldBe("ada@example.com");
        row.QuerySelector(".ts-unassigned .visually-hidden")!.TextContent.ShouldBe("Unassigned");
        row.QuerySelector(".ts-stamp")!.TextContent.ShouldBe("Spam?");
    }

    [Fact]
    public void The_table_has_column_headers_and_the_result_count_is_announced()
    {
        var cut = RenderQueue("open");

        cut.FindAll("thead th[scope=col]").Count.ShouldBe(9);
        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBe("2 of 2 tickets");
        cut.Find("h1").TextContent.ShouldBe("Queue");
    }

    [Fact]
    public void While_the_list_is_loading_the_page_shows_skeleton_rows_then_the_rows()
    {
        var gate = new TaskCompletionSource<Result<PagedResponse<TicketSummaryDto>>>();
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = RenderQueue("open");

        cut.FindAll(".ts-skeleton").Count.ShouldBe(8);
        cut.FindAll("tbody tr").ShouldBeEmpty();

        gate.SetResult(TestData.Ok(TestData.Page([TestData.Summary()])));

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_an_alert_with_the_API_message_and_retry_reloads()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<TicketSummaryDto>>("status-invalid", "Status must be one of New, Open."),
            TestData.Ok(TestData.Page([TestData.Summary()])));

        var cut = RenderQueue("open");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load tickets. Status must be one of New, Open.");

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_refresh_keeps_the_rows_that_were_already_on_screen()
    {
        var cut = RenderQueue("open");
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<PagedResponse<TicketSummaryDto>>("boom", "The API is unavailable."));

        cut.FindAll("button").Single(b => b.TextContent == "Refresh").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("mine")]
    [InlineData("open")]
    public void A_truly_empty_Unassigned_Mine_or_Open_view_shows_the_All_caught_up_brand_moment(string slug)
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));

        var cut = RenderQueue(slug);

        cut.Find(".ts-window-title").TextContent.ShouldBe("queue.exe — 0 items");
        cut.Find(".ts-window h2").TextContent.ShouldBe("All caught up");
        cut.Find(".ts-window p").TextContent.ShouldBe("Zero tickets, fully supported.");
        var link = cut.Find(".ts-window-link");
        link.TextContent.ShouldBe(slug == "open" ? "View all tickets" : "View open tickets");
        link.GetAttribute("href").ShouldBe(slug == "open" ? "/queue/all" : "/queue/open");
    }

    [Theory]
    [InlineData("pending", "No pending tickets")]
    [InlineData("all", "No tickets yet")]
    [InlineData("spam", "No spam")]
    public void The_other_empty_views_are_plain_text_without_the_brand_window(string slug, string heading)
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));

        var cut = RenderQueue(slug);

        cut.Find(".ts-state-heading").TextContent.ShouldBe(heading);
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public void A_filtered_empty_result_says_no_tickets_match_and_clearing_filters_goes_back_to_the_bare_view()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));
        var cut = RenderQueue("open", "?search=zzz&status=Pending");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No tickets match");
        cut.FindAll(".ts-window").ShouldBeEmpty();

        cut.FindAll(".ts-state button").Single().Click();

        _navigation.Uri.ShouldEndWith("/queue/open");
    }

    [Fact]
    public void The_filters_in_the_query_string_become_the_request()
    {
        var productId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        RenderQueue("pending", $"?product={productId}&status=Pending&priority=High&tag={tagId}&search=refund%20policy&page=3");

        var request = Requests().Single();
        request.View.ShouldBe(TicketViews.Pending);
        request.ProductId.ShouldBe(productId);
        request.Status.ShouldBe("Pending");
        request.Priority.ShouldBe("High");
        request.TagId.ShouldBe(tagId);
        request.Search.ShouldBe("refund policy");
        request.Page.ShouldBe(3);
        request.PageSize.ShouldBe(25);
    }

    [Fact]
    public void Typing_in_search_issues_one_call_after_the_debounce_and_replaces_the_history_entry()
    {
        var cut = RenderQueue("open");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("refund");
        Time.Advance(QueueDefaults.SearchDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().Select(r => r.Search).ShouldBe(["refund"]));
        Time.Advance(QueueDefaults.SearchDebounce * 3);
        Requests().Select(r => r.Search).ShouldBe(["refund"]);
        _navigation.Uri.ShouldEndWith("/queue/open?search=refund");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void Pressing_Enter_in_search_does_not_wait_for_the_debounce()
    {
        var cut = RenderQueue("open");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("refund");
        cut.Find("form[role=search]").Submit();

        cut.WaitForAssertion(() => Requests().Select(r => r.Search).ShouldBe(["refund"]));
    }

    [Fact]
    public void A_new_search_goes_back_to_page_one_and_clearing_the_text_removes_the_filter()
    {
        var cut = RenderQueue("open", "?search=old&page=4");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("");
        cut.Find("form[role=search]").Submit();

        cut.WaitForAssertion(() => Requests().Select(r => (r.Search, r.Page)).ShouldBe([((string?)null, 1)]));
    }

    [Fact]
    public void Choosing_a_product_status_priority_or_tag_requests_page_one_with_that_filter()
    {
        var cut = RenderQueue("all", "?page=2");
        _tickets.ClearReceivedCalls();

        cut.Find("#queue-product").Change(TestData.OrbitlyId.ToString());
        cut.WaitForAssertion(() => Requests().Last().ProductId.ShouldBe(TestData.OrbitlyId));
        Requests().Last().Page.ShouldBe(1);

        cut.Find("#queue-status").Change("Solved");
        cut.WaitForAssertion(() => Requests().Last().Status.ShouldBe("Solved"));

        cut.Find("#queue-priority").Change("Urgent");
        cut.WaitForAssertion(() => Requests().Last().Priority.ShouldBe("Urgent"));

        cut.Find("#queue-tag").Change(TestData.BugTagId.ToString());
        cut.WaitForAssertion(() => Requests().Last().TagId.ShouldBe(TestData.BugTagId));

        var last = Requests().Last();
        (last.ProductId, last.Status, last.Priority, last.Page).ShouldBe((TestData.OrbitlyId, "Solved", "Urgent", 1));
    }

    [Fact]
    public void Changing_the_page_keeps_every_filter()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.Page([TestData.Summary()], page: 1, total: 60)));
        var cut = RenderQueue("open", "?status=Pending&search=refund");
        _tickets.ClearReceivedCalls();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        cut.WaitForAssertion(() => Requests().Select(r => (r.Status, r.Search, r.Page)).ShouldBe([("Pending", "refund", 2)]));
        _navigation.Uri.ShouldEndWith("/queue/open?status=Pending&search=refund&page=2");
    }

    [Fact]
    public async Task J_and_k_move_the_selection_without_reordering_and_enter_opens_the_selected_ticket()
    {
        var cut = RenderQueue("open");
        cut.FindAll("tr[aria-current]").ShouldBeEmpty();

        await PressAsync("j");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-1");
        await PressAsync("ArrowDown");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
        await PressAsync("j");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
        await PressAsync("k");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-1");
        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-1", "ORB-2"]);

        await PressAsync("Enter");

        _navigation.Uri.ShouldEndWith("/tickets/ORB-1");
        _queueModule.Invocations["scrollSelectedIntoView"].Count.ShouldBe(4);
    }

    [Fact]
    public async Task Enter_with_nothing_selected_and_keys_pressed_while_typing_do_nothing()
    {
        var cut = RenderQueue("open");
        var before = _navigation.Uri;

        await PressAsync("Enter");
        await PressAsync("j", typing: true);

        _navigation.Uri.ShouldBe(before);
        cut.FindAll("tr[aria-current]").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_slash_key_focuses_the_search_box()
    {
        var cut = RenderQueue("open");

        await PressAsync("/");

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(cut.Find("input[type=search]"));
    }

    [Fact]
    public async Task A_disposed_page_no_longer_answers_shortcuts()
    {
        var cut = RenderQueue("open");
        await cut.Instance.DisposeAsync();

        await PressAsync("j");

        cut.FindAll("tr[aria-current]").ShouldBeEmpty();
    }

    [Fact]
    public void Every_client_call_receives_the_page_s_cancellation_token()
    {
        RenderQueue("open");

        var tokens = _tickets.ReceivedCalls().Concat(_products.ReceivedCalls()).Concat(_tags.ReceivedCalls())
            .Select(call => call.GetArguments().Last()).OfType<CancellationToken>().ToList();
        tokens.Count.ShouldBe(4);
        tokens.ShouldAllBe(token => token.CanBeCanceled);
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_valid_page_instead_of_claiming_the_queue_is_empty()
    {
        _tickets.ListAsync(Arg.Is<ListTicketsRequest>(r => r.Page == 999), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([], page: 999, total: 60)));
        _tickets.ListAsync(Arg.Is<ListTicketsRequest>(r => r.Page == 3), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([TestData.Summary("ORB-60")], page: 3, total: 60)));

        var cut = RenderQueue("open", "?page=999");

        cut.WaitForAssertion(() => cut.Find("tbody tr").GetAttribute("data-ticket").ShouldBe("ORB-60"));
        _navigation.Uri.ShouldEndWith("/queue/open?page=3");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public void A_page_past_the_end_of_an_empty_result_goes_to_page_one_and_shows_the_empty_state_there()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([], page: 1, total: 0)));

        var cut = RenderQueue("pending", "?page=4");

        cut.WaitForAssertion(() => cut.Find(".ts-state-heading").TextContent.ShouldBe("No pending tickets"));
        _navigation.Uri.ShouldEndWith("/queue/pending");
        Requests().Select(r => r.Page).ShouldBe([4, 1]);
    }

    [Theory]
    [InlineData("?product=not-a-guid&tag=zzz", null, null)]
    [InlineData("?page=-5", null, null)]
    [InlineData("?page=abc", null, null)]
    [InlineData("?page=0", null, null)]
    [InlineData("?status=Bogus&priority=Nope", null, null)]
    [InlineData("?status=pending&priority=URGENT", "Pending", "Urgent")]
    [InlineData("?view=Spam", null, null)]
    public void Tampered_query_values_are_dropped_and_never_reach_the_API(string query, string? status, string? priority)
    {
        RenderQueue("open", query);

        var request = Requests().Single();
        request.View.ShouldBe(TicketViews.Open);
        request.Page.ShouldBe(1);
        request.ProductId.ShouldBeNull();
        request.TagId.ShouldBeNull();
        request.Status.ShouldBe(status);
        request.Priority.ShouldBe(priority);
    }

    [Fact]
    public async Task Enter_escapes_a_reserved_character_in_the_ticket_number()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.Page([TestData.Summary("ORB/1?x")])));
        RenderQueue("open");

        await PressAsync("j");
        await PressAsync("Enter");

        _navigation.Uri.ShouldEndWith("/tickets/ORB%2F1%3Fx");
    }

    [Fact]
    public void A_throwing_client_shows_the_error_state_and_clears_the_loading_state()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<PagedResponse<TicketSummaryDto>>>>(_ => throw new HttpRequestException("secret host 10.1.2.3"));

        var cut = RenderQueue("open");

        cut.Find("[role=alert] p").TextContent.ShouldStartWith("Couldn't load tickets.");
        cut.Markup.ShouldNotContain("10.1.2.3");
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();
    }

    [Fact]
    public void A_throwing_lookup_client_leaves_those_filters_empty_and_the_list_working()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns<Task<Result<IReadOnlyList<ProductDto>>>>(_ => throw new HttpRequestException("boom"));

        var cut = RenderQueue("open");

        cut.FindAll("tbody tr").Count.ShouldBe(2);
        cut.FindAll("#queue-product option").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_keeps_the_selection_on_the_same_ticket_even_when_it_moves()
    {
        var first = TestData.Summary("ORB-1");
        var second = TestData.Summary("ORB-2");
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([first, second])));
        var cut = RenderQueue("open");
        await PressAsync("j");
        await PressAsync("j");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");

        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.Page([TestData.Summary("ORB-0"), first, second])));
        cut.FindAll("button").Single(b => b.TextContent == "Refresh").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(3));
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
    }
}
