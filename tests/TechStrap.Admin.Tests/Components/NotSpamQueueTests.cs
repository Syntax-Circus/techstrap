using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>PHASE-07 T22 on the queue side: the Spam view's Not spam (the u key and the row button).</summary>
public sealed class NotSpamQueueTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;

    public NotSpamQueueTests()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Page(
            [TestData.Summary("ORB-1", isSpam: true), TestData.Summary("ORB-2", "Free money", isSpam: true)])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts(spam: 2)), TestData.Ok(TestData.Counts(spam: 1)));
        _tickets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Detail("ORB-1", isSpam: true, rowVersion: 11)));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: false, rowVersion: 12)));
        var products = Substitute.For<IProductsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(tags);
        JSInterop.SetupModule("./js/queue.js").SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string view)
    {
        _navigation.NavigateTo($"/queue/{view}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private int SpamWrites() =>
        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.SetSpamAsync));

    [Fact]
    public void Only_the_Spam_view_has_the_Not_spam_button_and_its_column()
    {
        var spam = RenderQueue("spam");

        spam.FindAll("tbody tr").ShouldAllBe(row => row.QuerySelector("td.ts-col-actions button")!.TextContent.Contains("Not spam"));
        spam.FindAll("thead th").Count.ShouldBe(10);

        var open = Render<TicketQueuePage>(p => p.Add(c => c.View, "open"));
        open.FindAll("td.ts-col-actions").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_restores_the_selected_row_with_one_read_one_write_and_no_dialog()
    {
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        await _tickets.Received(1).GetAsync("ORB-1", Arg.Any<CancellationToken>());
        await _tickets.Received(1).SetSpamAsync(
            Arg.Any<Guid>(), Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false && r.RowVersion == 11u), Arg.Any<CancellationToken>());
        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-2"]);
        StatusMessages.Current.ShouldBe("Restored ORB-1 from spam");
        cut.FindAll("dialog").ShouldBeEmpty();
        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBe("1 of 1 tickets");
        cut.Find(".ts-tab--spam .ts-count").TextContent.ShouldBe("1");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
    }

    [Fact]
    public void The_row_button_does_the_same_as_the_key()
    {
        var cut = RenderQueue("spam");

        cut.FindAll("td.ts-col-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-1"]));
        StatusMessages.Current.ShouldBe("Restored ORB-2 from spam");
        SpamWrites().ShouldBe(1);
    }

    [Fact]
    public async Task The_last_restored_row_leaves_the_plain_No_spam_state()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([TestData.Summary("ORB-1", isSpam: true)])));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No spam");
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_does_nothing_with_no_selection_while_typing_with_the_layer_off_or_outside_the_Spam_view()
    {
        var spam = RenderQueue("spam");

        await PressAsync("u");
        await PressAsync("j");
        await PressAsync("u", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("u");

        SpamWrites().ShouldBe(0);
        spam.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_u_key_does_nothing_in_a_normal_view()
    {
        RenderQueue("open");
        await PressAsync("j");

        await PressAsync("u");

        SpamWrites().ShouldBe(0);
        await _tickets.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pressing_u_twice_while_the_first_is_running_writes_once()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        RenderQueue("spam");
        await PressAsync("j");

        var first = PressAsync("u");
        await PressAsync("u");

        SpamWrites().ShouldBe(1);
        gate.SetResult(TestData.Ok(TestData.State(isSpam: false)));
        await first;
    }

    [Fact]
    public async Task A_conflict_keeps_the_row_and_tells_the_agent_to_open_the_ticket()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("ORB-1 changed meanwhile. Open it to review, then restore it from spam.");
        cut.FindAll("tbody tr").Count.ShouldBe(2);
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_read_or_write_shows_the_API_message_and_keeps_the_row()
    {
        _tickets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't restore the ticket from spam. The API is unavailable.");
        SpamWrites().ShouldBe(0);
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_closed_ticket_refused_by_the_API_shows_the_API_s_reason()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketClosed, "Closed tickets are read-only.", ResultErrorKind.Conflict));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't restore the ticket from spam. Closed tickets are read-only.");
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_uncertain_write_says_it_may_have_been_applied_and_offers_reload_never_try_again()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ApiTimeout, "Slow."));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe(ActionsCopy.RestoreUncertain);
        cut.Find("[role=alert] button").TextContent.ShouldBe(ActionsCopy.Reload);
        cut.Markup.ShouldNotContain("Try again");
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_restore_write_is_never_cancelled()
    {
        CancellationToken seen = default;
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            seen = call.Arg<CancellationToken>();
            return TestData.Ok(TestData.State(isSpam: false, rowVersion: 12));
        });
        RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        seen.CanBeCanceled.ShouldBeFalse();
    }
}
