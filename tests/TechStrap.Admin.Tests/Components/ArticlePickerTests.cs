using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The article picker of the reply composer (PHASE-08 T20): it searches published articles of the ticket's product and the shared ones, only after the agent types and after the debounce, never offers a draft or an
/// archived article, and reports a choice without keeping the selection itself. Review Focus 5 (the Admin half): a reply never offers what the API would refuse to link.
/// </summary>
public sealed class ArticlePickerTests : AdminComponentTest
{
    private static readonly Guid Other = Guid.Parse("dddddddd-0000-0000-0000-0000000000a2");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly List<ArticleChoice> _added = [];
    private readonly CountingTimeProvider _timers;

    public ArticlePickerTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: TestData.ArticleId),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Published, productId: null, id: Other),
        ])));
        Services.AddSingleton(_kb);
    }

    private IRenderedComponent<ArticlePicker> RenderPicker(IReadOnlyList<ArticleChoice>? selected = null, bool disabled = false, Guid? productId = null) =>
        Render<ArticlePicker>(p => p
            .Add(c => c.ProductId, productId ?? TestData.OrbitlyId)
            .Add(c => c.Selected, selected ?? [])
            .Add(c => c.Disabled, disabled)
            .Add(c => c.OnAdd, choice => _added.Add(choice)));

    private IEnumerable<ListKbArticlesRequest> Requests() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(c => (ListKbArticlesRequest)c.GetArguments()[0]!);

    private void Search(IRenderedComponent<ArticlePicker> cut, string text)
    {
        cut.Find("input[type=search]").Input(text);
        Time.Advance(KbDefaults.PickerDebounce);
    }

    [Fact]
    public void Nothing_is_called_until_the_agent_types()
    {
        var cut = RenderPicker();
        Time.Advance(KbDefaults.PickerDebounce * 3);

        Requests().ShouldBeEmpty();
        cut.FindAll("ul").ShouldBeEmpty();
        cut.Find("label").TextContent.ShouldBe("Search published articles");
    }

    [Fact]
    public void Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones()
    {
        var cut = RenderPicker();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("reset");
        Time.Advance(KbDefaults.PickerDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();
        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().ShouldBe([new ListKbArticlesRequest(TestData.OrbitlyId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, null, "reset", 1, 10)]));
        KbDefaults.PickerDebounce.ShouldBe(TimeSpan.FromMilliseconds(300));
        KbDefaults.PickerPageSize.ShouldBe(10);
    }

    [Fact]
    public void Results_show_their_title_and_a_shared_mark_and_an_add_button_that_names_the_article()
    {
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));
        var items = cut.FindAll("ul.ts-kb-picker-results li");
        items[0].QuerySelector(".ts-kb-picker-title")!.TextContent.ShouldBe("Reset your password");
        items[0].QuerySelectorAll(".ts-pill").ShouldBeEmpty();
        items[1].QuerySelector(".ts-pill")!.TextContent.ShouldBe("Shared");
        items[0].QuerySelector("button")!.GetAttribute("aria-label").ShouldBe("Add Reset your password");
        cut.Find("[role=status].visually-hidden").TextContent.ShouldBe("2 articles");
    }

    // Review Focus 5: a draft or an archived article is never offered, whatever the answer holds.
    [Fact]
    public void A_row_that_is_not_published_is_never_offered_even_if_the_answer_holds_one()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Live", "live", KbArticleStatuses.Published, id: Guid.NewGuid()),
            TestData.KbItem("A draft", "draft", KbArticleStatuses.Draft, id: Guid.NewGuid()),
            TestData.KbItem("Old", "old", KbArticleStatuses.Archived, id: Guid.NewGuid()),
        ])));
        var cut = RenderPicker();

        Search(cut, "x");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Select(l => l.QuerySelector(".ts-kb-picker-title")!.TextContent).ShouldBe(["Live"]));
        cut.Markup.ShouldNotContain("A draft");
        Requests().Single().Status.ShouldBe("Published");
    }

    [Fact]
    public void Adding_reports_the_choice_and_keeps_no_selection_of_its_own()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.Click();

        _added.ShouldBe([new ArticleChoice(Other, "Welcome", IsShared: true)]);
        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.TextContent.ShouldBe("Add");
    }

    [Fact]
    public void An_article_that_is_already_selected_says_added_and_cannot_be_added_twice()
    {
        var cut = RenderPicker(selected: [new ArticleChoice(TestData.ArticleId, "Reset your password", false)]);
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        var first = cut.FindAll("ul.ts-kb-picker-results li")[0].QuerySelector("button")!;

        first.TextContent.ShouldBe("Added");
        first.HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void At_the_limit_every_add_is_off_and_the_picker_says_so()
    {
        var full = Enumerable.Range(0, TicketOperationLimits.MaxLinkedArticles).Select(i => new ArticleChoice(Guid.NewGuid(), $"Article {i}", false)).ToList();
        var cut = RenderPicker(selected: full);
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.FindAll("ul.ts-kb-picker-results button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find(".ts-kb-picker-state[role=status]").TextContent.ShouldBe("A reply can link up to 10 articles.");
        cut.FindAll("ul.ts-kb-picker-results li")[0].QuerySelector("button")!.Click();
        _added.ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_picker_cannot_search_or_add()
    {
        var cut = RenderPicker(disabled: true);

        cut.Find("input[type=search]").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Blank_text_clears_the_results_and_calls_nothing()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        Search(cut, "   ");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results").ShouldBeEmpty());
        Requests().Count().ShouldBe(1);
        cut.FindAll(".ts-kb-picker-state").ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_with_no_published_match_says_so()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));
        var cut = RenderPicker();

        Search(cut, "nothing");

        cut.WaitForAssertion(() => cut.Find(".ts-kb-picker-state").TextContent.ShouldBe("No published article matches."));
    }

    [Fact]
    public void A_failed_search_says_so_in_fixed_words_and_never_shows_the_api_message()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "<img src=x onerror=alert(1)>"));
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't search the articles. Try again in a moment."));
        cut.Markup.ShouldNotContain("onerror");
    }

    [Fact]
    public void A_search_that_throws_is_a_failed_search_and_never_reaches_the_renderer()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns<Task<Result<PagedResponse<KbArticleListItemDto>>>>(_ => throw new InvalidOperationException("secret host:8080"));
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't search"));
        cut.Markup.ShouldNotContain("secret");
    }

    [Fact]
    public async Task An_answer_that_was_overtaken_by_a_newer_search_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<KbArticleListItemDto>>>();
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Text == "old"), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Text == "new"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Newer", "newer", KbArticleStatuses.Published, id: Guid.NewGuid())])));
        var cut = RenderPicker();
        Search(cut, "old");
        cut.WaitForAssertion(() => Requests().Count().ShouldBe(1));

        Search(cut, "new");
        cut.WaitForAssertion(() => cut.Find(".ts-kb-picker-title").TextContent.ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbPage([TestData.KbItem("Older", "older", KbArticleStatuses.Published, id: Guid.NewGuid())])));

        // The late answer is handled on the renderer's thread: let it run, then draw again, so a guard that is missing would show.
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(c => c.Disabled, false));
        cut.Find(".ts-kb-picker-title").TextContent.ShouldBe("Newer");
        cut.Markup.ShouldNotContain("Older");
    }

    [Fact]
    public void A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone()
    {
        var cut = RenderPicker();
        cut.Find("input[type=search]").Input("reset");
        _timers.LiveTimers.ShouldBe(1);

        cut.Instance.Dispose();

        // Released at once, not when it would have fired.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(KbDefaults.PickerDebounce * 3);
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_that_moves_to_another_product_stops_offering_the_old_products_articles()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.Render(p => p.Add(c => c.ProductId, Guid.NewGuid()).Add(c => c.Selected, []).Add(c => c.OnAdd, choice => _added.Add(choice)));

        cut.FindAll("ul.ts-kb-picker-results").ShouldBeEmpty();
        Search(cut, "reset");
        cut.WaitForAssertion(() => Requests().Last().ProductId.ShouldNotBe(TestData.OrbitlyId));
    }
}
