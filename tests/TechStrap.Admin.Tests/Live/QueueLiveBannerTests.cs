using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The queue's live behavior (T13): a change by another agent, or a resync, raises ONE banner after a one-second window; the agent's own changes raise none; nothing reloads or reorders until the banner is
/// clicked, and the click is the ordinary load with the current filters. The page never starts the connection (the indicator does) and releases its timer and its subscription when it goes.
/// </summary>
public sealed class QueueLiveBannerTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;
    private readonly CountingTimeProvider _timers;

    // The requirement is a one-second window, written here as a literal on purpose: a change of the constant must fail these tests.
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    public QueueLiveBannerTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([TestData.Summary("ORB-1"), TestData.Summary("ORB-2", "Billing question")])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts()));
        var products = Substitute.For<IProductsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        var tags = Substitute.For<ITagsClient>();
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(tags);
        JSInterop.SetupModule("./js/queue.js").SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string? view = null, string query = "")
    {
        _navigation.NavigateTo($"/queue/{view}{query}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private int ListCalls() => _tickets.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync));

    private static string[] RowNumbers(IRenderedComponent<TicketQueuePage> cut) => [.. cut.FindAll("tbody tr").Select(row => row.QuerySelector("a")!.GetAttribute("href")!)];

    [Fact(Timeout = 30000)]
    public async Task A_change_by_another_agent_raises_the_banner_after_the_window_and_reloads_and_reorders_nothing()
    {
        var cut = RenderQueue();
        var rows = RowNumbers(cut);

        LiveClient.RaiseChange(Change(actor: ColleagueId));
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond - TimeSpan.FromMilliseconds(1));
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        Time.Advance(TimeSpan.FromMilliseconds(1));

        await cut.WaitForAssertionAsync(() => cut.Find(".ts-live-banner button").TextContent.ShouldBe(LiveCopy.QueueUpdated)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        ListCalls().ShouldBe(1);
        RowNumbers(cut).ShouldBe(rows);
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact(Timeout = 30000)]
    public async Task A_burst_of_changes_gives_one_banner_and_one_timer()
    {
        var cut = RenderQueue();

        for (var i = 0; i < 20; i++)
        {
            LiveClient.RaiseChange(Change(ticketId: Guid.NewGuid(), actor: ColleagueId));
        }

        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);

        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        LiveClient.RaiseChange(Change(actor: ColleagueId));

        // The renderer runs its queued work in order, so once this returns the change has been handled: no second timer was started for it.
        await cut.InvokeAsync(() => { });
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(TimeSpan.FromSeconds(5));
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void The_agents_own_change_raises_no_banner_and_starts_no_timer()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change(actor: MeId));
        Time.Advance(TimeSpan.FromSeconds(5));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact(Timeout = 30000)]
    public async Task A_change_with_no_actor_raises_the_banner()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change() with { ActorAgentId = null });
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);

        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task A_resync_raises_the_banner_even_when_it_names_the_agent()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Resync() with { ActorAgentId = MeId });
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);

        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task The_banner_is_announced_in_the_polite_status_region()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);

        await cut.WaitForAssertionAsync(() => cut.Find("p.visually-hidden[role=status]").TextContent.ShouldContain(LiveCopy.QueueUpdated)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task Clicking_the_banner_reloads_with_the_current_filters_and_clears_it()
    {
        var cut = RenderQueue("mine", "?status=Open");
        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);
        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Find(".ts-live-banner button").Click();

        await cut.WaitForAssertionAsync(() => ListCalls().ShouldBe(2)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        var requests = _tickets.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync)).Select(call => (ListTicketsRequest)call.GetArguments()[0]!).ToList();
        requests[1].ShouldBe(requests[0]);
        requests[1].View.ShouldBe(TicketViews.Mine);
        requests[1].Status.ShouldBe(TicketStatuses.Open);
    }

    [Fact(Timeout = 30000)]
    public async Task The_refresh_button_of_the_filter_bar_clears_the_banner_too()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Time.Advance(OneSecond);
        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == QueueCopy.Refresh).Click();

        await cut.WaitForAssertionAsync(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty()).WaitAsync(Xunit.TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task A_timer_that_fired_just_before_a_reload_does_not_raise_a_banner_after_it()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var late = _timers.LastCallback.ShouldNotBeNull();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == QueueCopy.Refresh).Click();
        await cut.WaitForAssertionAsync(() => ListCalls().ShouldBe(2)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The timer had fired before the reload, but its work reaches the renderer only now: it belongs to a banner the reload has already answered.
        await Task.Run(() => late(null), Xunit.TestContext.Current.CancellationToken);
        await cut.InvokeAsync(() => { });

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task A_reload_cancels_a_pending_banner_and_its_timer()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == QueueCopy.Refresh).Click();
        await cut.WaitForAssertionAsync(() => ListCalls().ShouldBe(2)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The reload showed everything up to now, so the pending banner is canceled with its timer.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(TimeSpan.FromSeconds(5));
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
    }

    [Fact]
    public void Switched_off_a_change_raises_nothing()
    {
        LiveClient.IsEnabled = false;
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change());
        Time.Advance(TimeSpan.FromSeconds(5));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void The_page_never_starts_the_connection_and_never_joins_a_ticket()
    {
        RenderQueue();

        LiveClient.StartCalls.ShouldBe(0);
        LiveClient.Joined.ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_unsubscribes_and_releases_a_pending_timer()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        await cut.WaitForAssertionAsync(() => _timers.LiveTimers.ShouldBe(1)).WaitAsync(Xunit.TestContext.Current.CancellationToken);

        await cut.Instance.DisposeAsync();

        LiveClient.HasSubscribers.ShouldBeFalse();
        _timers.LiveTimers.ShouldBe(0);
        Should.NotThrow(() => LiveClient.RaiseChange(Change()));
        Should.NotThrow(() => Time.Advance(TimeSpan.FromSeconds(5)));
    }
}
