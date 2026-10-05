using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Audit;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

public sealed class AdminEventsPageTests : AdminPageTest
{
    private readonly IAdminEventsClient _events = Substitute.For<IAdminEventsClient>();
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly NavigationManager _navigation;

    public AdminEventsPageTests()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(new PagedResponse<AdminEventDto>(
        [
            TestData.AdminEvent(AdminEventTypes.TagDeleted, "{\"slug\":\"bug\",\"detachedTicketCount\":2}", at: TestData.Now.AddMinutes(-5)),
            TestData.AdminEvent(AdminEventTypes.ApiKeyRevoked, "{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"keyPrefix\":\"tsk_ab12\"}", AdminSubjectTypes.ApiKey, actor: null, at: TestData.Now.AddHours(-2)),
        ], 1, 25, 2)));
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>(
            [TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin), TestData.AgentRow("Sam Ortiz", TestData.SamAgentId)]));
        Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        Services.AddSingleton(_events);
        Services.AddSingleton(_agents);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<AdminEventsPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/settings/audit{query}");
        return Render<AdminEventsPage>();
    }

    private IReadOnlyList<(AdminEventFilter Filter, int Page, int PageSize)> Requests() =>
        [.. _events.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAdminEventsClient.ListAsync))
            .Select(c => ((AdminEventFilter)c.GetArguments()[0]!, (int)c.GetArguments()[1]!, (int)c.GetArguments()[2]!))];

    // ---- what it shows -------------------------------------------------------------------------------------------

    [Fact]
    public void Events_are_listed_in_the_order_the_api_sent_with_when_who_what_and_one_sentence()
    {
        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows.Count.ShouldBe(2);
        rows[0].Children[0].TextContent.ShouldBe("5 min ago");
        rows[0].Children[1].TextContent.ShouldBe("Ada Admin");
        rows[0].Children[2].TextContent.ShouldBe("Tag");
        rows[0].Children[3].TextContent.ShouldBe("Deleted tag bug, removed from 2 tickets");
        rows[1].Children[1].TextContent.ShouldBe("Unknown agent");
        rows[1].Children[2].TextContent.ShouldBe("API key");
        rows[1].Children[3].TextContent.ShouldBe("Revoked API key tsk_ab12");
        cut.Find("h1").TextContent.ShouldBe("Audit log");
    }

    [Fact]
    public void A_hostile_payload_is_encoded_and_the_raw_json_is_never_rendered()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AdminEventDto>(
        [
            TestData.AdminEvent(AdminEventTypes.TagCreated, "{\"slug\":\"<img src=x onerror=alert(1)>\",\"secret\":\"hunter2\"}"),
            TestData.AdminEvent("SomethingNew", "{\"slug\":\"x\",\"token\":\"abc123\"}"),
        ], 1, 25, 2)));

        var cut = RenderPage();

        cut.FindAll("img").ShouldBeEmpty();
        cut.Markup.ShouldContain("&lt;img src=x onerror=alert(1)&gt;");
        cut.Markup.ShouldNotContain("\"slug\"");
        cut.Markup.ShouldNotContain("hunter2");
        cut.Markup.ShouldNotContain("abc123");
        cut.FindAll("tbody tr")[1].Children[3].TextContent.ShouldBe("Something new");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<AdminEventDto>>("api-error", "The API is unavailable."),
            TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent()], 1, 25, 1)));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the audit log. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void No_events_at_all_is_a_plain_empty_state_and_no_matches_is_a_different_one_with_a_way_back()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 1, 25, 0)));

        var plain = RenderPage();
        plain.Find(".ts-state-heading").TextContent.ShouldBe("No audit events yet");
        plain.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_filter_with_no_matches_says_so_and_offers_to_clear_the_filters()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 1, 25, 0)));

        var cut = RenderPage("?subject=Ticket");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No events match these filters");
        cut.Find(".ts-state--empty a").GetAttribute("href").ShouldBe("/settings/audit");
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_neither_the_log_nor_the_agent_list_is_asked_for()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _events.ReceivedCalls().ShouldBeEmpty();
        _agents.DidNotReceive().ListAllAsync(Arg.Any<CancellationToken>());
    }

    // ---- filters -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_filters_offer_every_subject_type_and_every_agent()
    {
        var cut = RenderPage();

        cut.Find("#ts-audit-subject").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(
            ["Everything", "Product", "API key", "Agent", "Tag", "Requester", "Ticket", "Email"]);
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Anyone", "Ada Admin", "Sam Ortiz"]);
    }

    [Fact]
    public void Filters_in_the_query_string_are_sent_in_canonical_form_and_shown_as_selected()
    {
        var cut = RenderPage($"?subject=tag&actor={TestData.SamAgentId}");

        var request = Requests().ShouldHaveSingleItem();
        request.Filter.SubjectType.ShouldBe("Tag");
        request.Filter.ActorId.ShouldBe(TestData.SamAgentId);
        cut.Find("#ts-audit-subject option[selected]").TextContent.ShouldBe("Tag");
        cut.Find("#ts-audit-actor option[selected]").TextContent.ShouldBe("Sam Ortiz");
    }

    [Fact]
    public void An_unknown_subject_or_a_bad_actor_in_the_address_is_dropped_and_never_sent()
    {
        RenderPage("?subject=Everything%27%3BDROP&actor=not-a-guid");

        var request = Requests().ShouldHaveSingleItem();
        request.Filter.SubjectType.ShouldBeNull();
        request.Filter.ActorId.ShouldBeNull();
    }

    [Fact]
    public void Choosing_a_subject_navigates_with_the_filter_in_the_query_string_keeping_the_actor_and_dropping_the_page()
    {
        var cut = RenderPage($"?actor={TestData.AdaAgentId}&page=3");

        cut.Find("#ts-audit-subject").Change("Ticket");

        _navigation.Uri.ShouldEndWith($"/settings/audit?subject=Ticket&actor={TestData.AdaAgentId}");
    }

    [Fact]
    public void Choosing_everything_removes_the_filter_from_the_address()
    {
        var cut = RenderPage("?subject=Tag");

        cut.Find("#ts-audit-subject").Change(string.Empty);

        _navigation.Uri.ShouldEndWith("/settings/audit");
    }

    [Fact]
    public void Choosing_an_actor_navigates_with_the_actor_in_the_query_string()
    {
        var cut = RenderPage("?subject=Tag");

        cut.Find("#ts-audit-actor").Change(TestData.SamAgentId.ToString());

        _navigation.Uri.ShouldEndWith($"/settings/audit?subject=Tag&actor={TestData.SamAgentId}");
    }

    [Fact]
    public void The_filter_still_works_when_the_agent_list_cannot_be_read()
    {
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<AgentListItemDto>>("api-error", "Down."));

        var cut = RenderPage();

        cut.FindAll("tbody tr").Count.ShouldBe(2);
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Anyone"]);
    }

    [Fact]
    public void A_slow_answer_for_an_earlier_filter_is_ignored_when_a_later_one_has_already_been_shown()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<AdminEventDto>>>();
        _events.ListAsync(Arg.Is<AdminEventFilter>(f => f.SubjectType == null), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(slow.Task);
        _events.ListAsync(Arg.Is<AdminEventFilter>(f => f.SubjectType == "Tag"), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent(AdminEventTypes.TagCreated, "{\"slug\":\"fast\"}")], 1, 25, 1)));
        var cut = RenderPage();

        _navigation.NavigateTo("/settings/audit?subject=Tag");
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        slow.SetResult(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent(AdminEventTypes.TagCreated, "{\"slug\":\"slow\"}"), TestData.AdminEvent()], 1, 25, 2)));
        cut.FindComponent<AdminEventsContent>().Render();

        cut.FindAll("tbody tr").Count.ShouldBe(1);
        cut.Find("tbody tr").TextContent.ShouldContain("Created tag fast");
        cut.Markup.ShouldNotContain("slow");
    }

    [Fact]
    public void The_events_are_read_without_waiting_for_the_agent_list()
    {
        var agentsGate = new TaskCompletionSource<Result<IReadOnlyList<AgentListItemDto>>>();
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(agentsGate.Task);

        var cut = RenderPage();

        Requests().Count.ShouldBe(1, "the events read starts while the filter's names are still on their way");
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(2));
        agentsGate.SetResult(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin)]));
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2));
    }

    [Fact]
    public void The_events_render_the_moment_they_arrive_while_the_agent_list_is_still_on_its_way()
    {
        var eventsGate = new TaskCompletionSource<Result<PagedResponse<AdminEventDto>>>();
        var agentsGate = new TaskCompletionSource<Result<IReadOnlyList<AgentListItemDto>>>();
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(eventsGate.Task);
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(agentsGate.Task);
        var cut = RenderPage();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        eventsGate.SetResult(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent(AdminEventTypes.TagDeleted, "{\"slug\":\"bug\",\"detachedTicketCount\":2}")], 1, 25, 1)));

        // The agent list has not answered: the rows are drawn without waiting for it, and the filter has only "everyone" until it does.
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(1);
        agentsGate.SetResult(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin)]));
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2));
    }

    [Fact]
    public void A_chosen_actor_stays_selected_while_the_names_are_on_their_way()
    {
        var agentsGate = new TaskCompletionSource<Result<IReadOnlyList<AgentListItemDto>>>();
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(agentsGate.Task);

        var cut = RenderPage($"?actor={TestData.SamAgentId}");

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(2));
        cut.Find("#ts-audit-actor option[selected]").GetAttribute("value").ShouldBe(TestData.SamAgentId.ToString());
        agentsGate.SetResult(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.AgentRow("Sam Ortiz", TestData.SamAgentId)]));
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor option[selected]").TextContent.ShouldBe("Sam Ortiz"));
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2);
    }

    [Fact]
    public void A_failing_agent_list_read_is_logged_by_type_name_only_and_the_events_still_render()
    {
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns<Task<Result<IReadOnlyList<AgentListItemDto>>>>(_ => throw new InvalidOperationException("secret ada@example.test"));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(2));
        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException")));
        _logs.Lines.ShouldNotContain(line => line.Contains("ada@example.test"));
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(1, "1 event")]
    [InlineData(2, "2 events")]
    [InlineData(0, "0 events")]
    public void The_total_is_announced_with_a_singular_or_plural_noun(int total, string expected)
    {
        var items = Enumerable.Range(0, Math.Min(total, 25)).Select(_ => TestData.AdminEvent()).ToArray();
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>(items, 1, 25, total)));

        var cut = RenderPage();

        cut.Find("p[role=status]").TextContent.ShouldBe(expected);
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_page_with_rows_and_never_says_there_are_no_events()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 99, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 99, 25, 30)));
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent()], 2, 25, 30)));

        var cut = RenderPage("?page=99");

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        _navigation.Uri.ShouldEndWith("/settings/audit?page=2");
        cut.Markup.ShouldNotContain("No audit events yet");
        cut.Markup.ShouldNotContain("No events match");
    }

    [Fact]
    public void A_filtered_page_past_the_end_keeps_the_filter_and_goes_to_the_first_page_when_that_is_the_last()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 5, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 5, 25, 3)));

        var cut = RenderPage("?subject=Tag&page=5");

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(2));
        _navigation.Uri.ShouldEndWith("/settings/audit?subject=Tag");
    }

    // ---- paging and asOf -----------------------------------------------------------------------------------------

    [Fact]
    public void The_first_page_asks_for_25_as_of_now()
    {
        RenderPage();

        var request = Requests().ShouldHaveSingleItem();
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.Filter.AsOf.ShouldBe(TestData.Now);
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent()], 1, 25, 60)));
        var cut = RenderPage("?subject=Tag");

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/settings/audit?subject=Tag&page=2");
    }

    [Fact]
    public void A_later_page_carries_the_asOf_of_the_first_page_even_when_time_has_moved_on()
    {
        RenderPage();
        Time.Advance(TimeSpan.FromMinutes(10));

        _navigation.NavigateTo("/settings/audit?page=2");

        var requests = Requests();
        requests.Count.ShouldBe(2);
        requests[1].Page.ShouldBe(2);
        requests[1].Filter.AsOf.ShouldBe(requests[0].Filter.AsOf);
        requests[1].Filter.AsOf.ShouldBe(TestData.Now);
    }

    [Fact]
    public void Going_back_to_the_first_page_or_changing_the_filter_or_refreshing_takes_a_new_asOf()
    {
        var cut = RenderPage();
        Time.Advance(TimeSpan.FromMinutes(10));
        _navigation.NavigateTo("/settings/audit?page=2");
        Time.Advance(TimeSpan.FromMinutes(10));

        _navigation.NavigateTo("/settings/audit");
        Time.Advance(TimeSpan.FromMinutes(10));
        _navigation.NavigateTo("/settings/audit?subject=Tag");
        Time.Advance(TimeSpan.FromMinutes(10));
        cut.Find("button.btn-outline-secondary").Click();

        Requests().Select(r => r.Filter.AsOf).ShouldBe(
        [
            TestData.Now,
            TestData.Now,
            TestData.Now.AddMinutes(20),
            TestData.Now.AddMinutes(30),
            TestData.Now.AddMinutes(40),
        ]);
    }

    [Fact]
    public void A_page_opened_directly_takes_now_as_its_asOf()
    {
        Time.Advance(TimeSpan.FromHours(1));

        RenderPage("?page=3");

        Requests().ShouldHaveSingleItem().Filter.AsOf.ShouldBe(TestData.Now.AddHours(1));
    }
}
