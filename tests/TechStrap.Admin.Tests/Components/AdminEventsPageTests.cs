using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
