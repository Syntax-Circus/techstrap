using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The detail page's live behaviour (T14, T15). The rule that matters: a change by someone else only raises a banner. The page keeps its model, its row version and the agent's draft until the banner is clicked, so a send
/// before that still meets the existing 409 and an agent never acts on a version they have not seen. Own changes are ignored; the page joins the ticket on load, leaves it on navigation and disposal, and shows who else is here.
/// </summary>
public sealed class TicketDetailLiveTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly CountingTimeProvider _timers;
    private readonly RecordingLoggerProvider _logs = new();

    public TicketDetailLiveTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        Services.AddLogging(logging => logging.AddProvider(_logs));
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
        Services.AddSingleton(Substitute.For<IRequestersClient>());
        Show(TestData.Detail());
    }

    private void Show(TicketDetailDto detail, string number = "ORB-42") =>
        _tickets.GetAsync(number, Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket(string number = "ORB-42") => Render<TicketDetailPage>(p => p.Add(c => c.Number, number));

    private int Loads(string number = "ORB-42") => _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.GetAsync) && (string)c.GetArguments()[0]! == number);

    private static string Banner(IRenderedComponent<TicketDetailPage> cut) => cut.Find(".ts-live-banner").TextContent.Trim();

    private async Task ChangeAsync(IRenderedComponent<TicketDetailPage> cut, TicketChangedDto change)
    {
        LiveClient.RaiseChange(change);

        // The renderer runs its queued work in order, so once this returns the page has handled the change.
        await cut.InvokeAsync(() => { });
    }

    [Fact]
    public void The_page_joins_its_ticket_after_it_loads_and_never_starts_the_connection()
    {
        var cut = RenderTicket();

        cut.Find(".ts-ticket").ShouldNotBeNull();
        LiveClient.Joined.ShouldBe([TicketId]);
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact(Timeout = 30000)]
    public async Task A_change_by_another_agent_raises_the_banner_and_changes_nothing_else()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(actor: ColleagueId));

        Banner(cut).ShouldContain(LiveCopy.NewActivity);
        cut.Find(".ts-live-banner").HasAttribute("role").ShouldBeFalse();
        Loads().ShouldBe(1);
        cut.FindAll(".ts-timeline *").Count.ShouldBeGreaterThan(0);
    }

    [Fact(Timeout = 30000)]
    public async Task The_banner_is_announced_through_a_status_region_that_is_in_the_page_before_it_has_any_text()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var cut = RenderTicket();

        // A live region that is inserted together with its text is often not announced, so the region exists first, empty.
        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBeEmpty();
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();

        await ChangeAsync(cut, Change(actor: ColleagueId));

        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBe(LiveCopy.NewActivity);
    }

    [Fact(Timeout = 30000)]
    public async Task A_change_with_no_actor_and_a_resync_raise_the_banner()
    {
        var cut = RenderTicket();

        await ChangeAsync(cut, Change() with { ActorAgentId = null });
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        cut.Find(".ts-live-banner button").Click();
        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty()).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        await ChangeAsync(cut, Resync() with { ActorAgentId = MeId });

        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task A_change_to_another_ticket_and_the_agents_own_change_raise_nothing()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(ticketId: OtherTicketId, actor: ColleagueId));
        await ChangeAsync(cut, Change(actor: MeId));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        Loads().ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task Clicking_the_banner_reloads_the_timeline_and_takes_the_new_row_version_and_clears_the_banner()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 12))));
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        Show(TestData.Detail(rowVersion: 11, messages: [TestData.Message(), TestData.Message(MessageAuthorTypes.Requester, bodyHtml: "<p>Still broken</p>")]));

        cut.Find(".ts-live-banner button").Click();

        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty()).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Loads().ShouldBe(2);
        cut.Markup.ShouldContain("Still broken");
        cut.Find("textarea").Input("Now fixed.");
        cut.FindAll(".ts-composer-actions button")[0].Click();
        await _tickets.Received(1).ReplyAsync(TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 11u), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 30000)]
    public async Task Until_the_click_the_page_keeps_its_row_version_so_a_send_still_gets_the_409_and_the_draft_survives()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderTicket();
        cut.Find("textarea").Input("A reply I must not lose.");

        await ChangeAsync(cut, Change(actor: ColleagueId));
        Show(TestData.Detail(rowVersion: 99));
        cut.FindAll(".ts-composer-actions button")[0].Click();

        await _tickets.Received(1).ReplyAsync(TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 7u && r.Body == "A reply I must not lose."), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
        cut.Find(".ts-conflict").GetAttribute("role").ShouldBe("alert");
        cut.Find("textarea").GetAttribute("value").ShouldBe("A reply I must not lose.");
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        Loads().ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task The_draft_is_never_touched_by_the_banner_or_by_the_reload_it_offers()
    {
        var cut = RenderTicket();
        cut.Find("textarea").Input("Half a reply");
        var drafts = Services.GetRequiredService<DraftStore>();

        await ChangeAsync(cut, Change(actor: ColleagueId));
        cut.Find("textarea").GetAttribute("value").ShouldBe("Half a reply");
        cut.Find(".ts-live-banner button").Click();
        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty()).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Find("textarea").GetAttribute("value").ShouldBe("Half a reply");
        drafts.Get(TicketId).PublicText.ShouldBe("Half a reply");
    }

    [Fact(Timeout = 30000)]
    public async Task A_change_that_arrives_while_the_reload_runs_keeps_the_banner()
    {
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        var release = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(_ => release.Task);

        cut.Find(".ts-live-banner button").Click();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        release.SetResult(TestData.Ok(TestData.Detail(rowVersion: 8)));

        await cut.WaitForAssertionAsync(() => Loads().ShouldBe(2)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await cut.InvokeAsync(() => { });
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task A_failed_reload_keeps_the_banner_and_the_page()
    {
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("api-unavailable", "Down.", ResultErrorKind.Failure));

        cut.Find(".ts-live-banner button").Click();

        await cut.WaitForAssertionAsync(() => Loads().ShouldBe(2)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        cut.Find(".ts-ticket").ShouldNotBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task Another_agents_presence_is_shown_and_the_agent_themself_is_not()
    {
        LiveClient.JoinResult = id => Presence(id, Viewer(MeId, "Sam Ortiz"), Viewer(ColleagueId, "Ada Admin"));

        var cut = RenderTicket();

        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is viewing")).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        cut.Find(".ts-presence").GetAttribute("aria-live").ShouldBe("polite");
        cut.Find(".ts-presence").GetAttribute("role").ShouldBe("status");
    }

    [Fact(Timeout = 30000)]
    public async Task A_presence_update_for_this_ticket_replaces_the_list_and_one_for_another_ticket_is_ignored()
    {
        var cut = RenderTicket();
        cut.Find(".ts-presence").TextContent.Trim().ShouldBeEmpty();

        LiveClient.RaisePresence(Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing)));
        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying")).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        LiveClient.RaisePresence(Presence(OtherTicketId, Viewer(Guid.NewGuid(), "Someone Else")));
        await cut.InvokeAsync(() => { });
        cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying");
        LiveClient.RaisePresence(Presence(TicketId));

        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.Trim().ShouldBeEmpty()).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task A_replying_hint_that_is_not_refreshed_lapses_after_the_servers_lease_and_a_refresh_extends_it()
    {
        var cut = RenderTicket();
        var replying = Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing));

        LiveClient.RaisePresence(replying);
        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying")).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds - 1));
        LiveClient.RaisePresence(replying);
        Time.Advance(TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds - 1));
        cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying");
        Time.Advance(TimeSpan.FromSeconds(1));

        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is viewing")).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact(Timeout = 30000)]
    public async Task A_lease_timer_that_fired_just_before_a_fresh_presence_message_does_not_expire_the_fresh_hint()
    {
        var cut = RenderTicket();
        var replying = Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing));
        LiveClient.RaisePresence(replying);
        await cut.WaitForAssertionAsync(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying")).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var late = _timers.LastCallback.ShouldNotBeNull();

        LiveClient.RaisePresence(replying);
        await cut.InvokeAsync(() => { });

        // The first timer had fired before the fresh message, but its work reaches the renderer only now: it must not expire the fresh hint or release the newer timer.
        await Task.Run(() => late(null), Xunit.TestContext.Current.CancellationToken);
        await cut.InvokeAsync(() => { });

        cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying");
        _timers.LiveTimers.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task Navigating_to_another_ticket_leaves_the_old_group_and_joins_the_new_one()
    {
        var other = Guid.Parse("dddddddd-0000-0000-0000-000000000043");
        Show(TestData.Detail(number: "ORB-43") with { Id = other }, "ORB-43");
        var cut = RenderTicket();

        cut.Render(p => p.Add(c => c.Number, "ORB-43"));

        await cut.WaitForAssertionAsync(() => LiveClient.Joined.ShouldBe([TicketId, other])).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        LiveClient.Left.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_refresh_of_the_same_ticket_does_not_join_again()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var cut = RenderTicket();
        LiveClient.Joined.Count.ShouldBe(1);

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        LiveClient.Joined.ShouldBe([TicketId]);
        LiveClient.Left.ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_leaves_the_group_unsubscribes_and_releases_the_presence_timer()
    {
        var cut = RenderTicket();
        LiveClient.RaisePresence(Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing)));
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Instance.Dispose();

        LiveClient.Left.ShouldBe([TicketId]);
        LiveClient.HasSubscribers.ShouldBeFalse();
        _timers.LiveTimers.ShouldBe(0);
        Should.NotThrow(() => LiveClient.RaiseChange(Change(actor: ColleagueId)));
        Should.NotThrow(() => LiveClient.RaisePresence(Presence(TicketId)));
    }

    [Fact(Timeout = 30000)]
    public async Task Switched_off_the_page_joins_nothing_and_draws_nothing_live()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        LiveClient.IsEnabled = false;
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(actor: ColleagueId));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        cut.FindAll(".ts-presence").ShouldBeEmpty();
        LiveClient.Joined.ShouldBeEmpty();
        LiveClient.Left.ShouldBeEmpty();
    }

    [Fact]
    public void A_failing_client_leaves_the_page_working_and_logs_only_the_type()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");

        var cut = RenderTicket();

        cut.Find(".ts-ticket").ShouldNotBeNull();
        _logs.Lines.ShouldContain(line => line.Contains("Joining the live group", StringComparison.Ordinal) && line.Contains("InvalidOperationException", StringComparison.Ordinal));

        cut.Instance.Dispose();

        _logs.Lines.ShouldContain(line => line.Contains("Leaving the live group", StringComparison.Ordinal) && line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    public async Task A_ticket_that_is_gone_leaves_its_group()
    {
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("not-found", "Gone.", ResultErrorKind.NotFound));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        await cut.WaitForAssertionAsync(() => LiveClient.Left.ShouldBe([TicketId])).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }
}
