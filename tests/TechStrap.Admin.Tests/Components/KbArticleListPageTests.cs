using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The article list: filters in the URL, one load per filter, the newest answer wins, and every empty and failed state (PHASE-08 T15).</summary>
public sealed class KbArticleListPageTests : AdminComponentTest
{
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;
    private readonly CountingTimeProvider _timers;

    public KbArticleListPageTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, TestData.AccountCategoryId, id: TestData.ArticleId),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Draft, productId: null, categoryId: null),
        ])));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>(
            [TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId)]));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly"), TestData.Product("Paperplane", PaperplaneId)]));
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<KbArticleListPage> RenderList(string query = "")
    {
        _navigation.NavigateTo($"/kb{query}");
        return Render<KbArticleListPage>();
    }

    private IEnumerable<ListKbArticlesRequest> Requests() =>
        _kb.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(call => (ListKbArticlesRequest)call.GetArguments()[0]!);

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_load_asks_for_page_one_of_25_with_no_filter_and_reads_the_lookups_once()
    {
        RenderList();

        var request = Requests().Single();
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.ProductId.ShouldBeNull();
        request.SharedOnly.ShouldBeFalse();
        request.Status.ShouldBeNull();
        request.Text.ShouldBeNull();
        _products.Received(1).ListAsync(Arg.Any<CancellationToken>());
        _kb.Received(1).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Each_row_links_to_its_editor_and_says_its_product_its_category_and_its_status_in_words()
    {
        var cut = RenderList();

        var rows = cut.FindAll("tbody tr");
        rows.Count.ShouldBe(2);
        var first = rows.Single(r => r.GetAttribute("data-article") == "reset-password");
        first.QuerySelector("a")!.GetAttribute("href").ShouldBe($"/kb/{TestData.ArticleId}");
        first.QuerySelector("a")!.TextContent.ShouldBe("Reset your password");
        first.Children[1].TextContent.ShouldBe("Orbitly");
        first.Children[2].TextContent.ShouldBe("Account");
        first.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Published");
        var second = rows.Single(r => r.GetAttribute("data-article") == "welcome");
        second.Children[1].TextContent.ShouldBe("Shared");
        second.Children[2].TextContent.ShouldBe("No category");
        second.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Draft");
    }

    [Fact]
    public void A_product_the_lookups_did_not_return_shows_a_fixed_phrase_and_never_its_id()
    {
        var unknown = Guid.NewGuid();
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem(productId: unknown)])));

        var cut = RenderList();

        cut.Find("tbody tr").Children[1].TextContent.ShouldBe("Another product");
        cut.Markup.ShouldNotContain(unknown.ToString());
    }

    [Fact]
    public void The_table_sits_in_a_named_scroll_region_and_the_pager_shows_the_total()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem()], page: 2, total: 60)));

        var cut = RenderList("?page=2");

        cut.Find("div.ts-scroll").GetAttribute("aria-label").ShouldBe("Articles");
        cut.Find(".ts-pager-summary").TextContent.ShouldContain("of 60");
    }

    // ---- filters in the URL --------------------------------------------------------------------------------------

    [Fact]
    public void The_filters_in_the_query_string_become_the_request_and_an_unknown_status_is_dropped()
    {
        var categoryId = Guid.NewGuid();

        RenderList($"?product={PaperplaneId}&category={categoryId}&status=published&search=refund%20policy&page=3");

        var request = Requests().Single();
        request.ProductId.ShouldBe(PaperplaneId);
        request.CategoryId.ShouldBe(categoryId);
        request.Status.ShouldBe("Published");
        request.Text.ShouldBe("refund policy");
        request.Page.ShouldBe(3);

        _kb.ClearReceivedCalls();
        _navigation.NavigateTo("/kb?status=Banana&product=not-a-guid&page=zero");
        Requests().Single().ShouldBe(new ListKbArticlesRequest(null, false, false, null, null, null, 1, 25));
    }

    [Fact]
    public void The_shared_choice_asks_for_the_shared_articles_only()
    {
        RenderList("?product=shared");

        var request = Requests().Single();
        request.SharedOnly.ShouldBeTrue();
        request.ProductId.ShouldBeNull();
    }

    [Fact]
    public void Choosing_a_product_a_status_or_shared_goes_to_page_one_with_that_filter_in_the_address()
    {
        var cut = RenderList("?page=2");
        _kb.ClearReceivedCalls();

        cut.Find("#kb-product").Change(PaperplaneId.ToString());
        cut.WaitForAssertion(() => Requests().Select(r => r.ProductId).ShouldBe([PaperplaneId]));
        _navigation.Uri.ShouldEndWith($"/kb?product={PaperplaneId}");

        cut.Find("#kb-product").Change("shared");
        cut.WaitForAssertion(() => Requests().Last().SharedOnly.ShouldBeTrue());
        _navigation.Uri.ShouldEndWith("/kb?product=shared");

        cut.Find("#kb-status").Change("Archived");
        cut.WaitForAssertion(() => Requests().Last().Status.ShouldBe("Archived"));
        _navigation.Uri.ShouldContain("status=Archived");
    }

    [Fact]
    public void Typing_in_search_issues_one_call_after_the_debounce_and_replaces_the_history_entry()
    {
        var cut = RenderList();
        _kb.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("refund");
        Time.Advance(KbDefaults.SearchDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().Select(r => r.Text).ShouldBe(["refund"]));
        Time.Advance(KbDefaults.SearchDebounce * 3);
        Requests().Select(r => r.Text).ShouldBe(["refund"]);
        _navigation.Uri.ShouldEndWith("/kb?search=refund");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void A_search_that_is_still_waiting_is_released_with_the_page_so_nothing_fires_after_it_is_gone()
    {
        var cut = RenderList();
        _kb.ClearReceivedCalls();
        cut.Find("input[type=search]").Input("refund");
        _timers.LiveTimers.ShouldBe(1);

        cut.Instance.Dispose();
        Time.Advance(KbDefaults.SearchDebounce * 3);

        _timers.LiveTimers.ShouldBe(0);
        Requests().ShouldBeEmpty();
        _navigation.Uri.ShouldNotContain("search=refund");
    }

    [Fact]
    public void Paging_goes_to_the_next_page_in_the_address()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(TestData.KbPage([TestData.KbItem()], call.Arg<ListKbArticlesRequest>().Page, 60)));
        var cut = RenderList();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        cut.WaitForAssertion(() => Requests().Last().Page.ShouldBe(2));
        _navigation.Uri.ShouldEndWith("/kb?page=2");
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_real_page_instead_of_showing_an_empty_list()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<ListKbArticlesRequest>().Page == 9
                ? TestData.Ok(TestData.KbPage([], page: 9, total: 30))
                : TestData.Ok(TestData.KbPage([TestData.KbItem()], call.Arg<ListKbArticlesRequest>().Page, 30)));

        var cut = RenderList("?page=9");

        cut.WaitForAssertion(() => Requests().Last().Page.ShouldBe(2));
        _navigation.Uri.ShouldEndWith("/kb?page=2");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    // ---- empty and failed states ---------------------------------------------------------------------------------

    [Fact]
    public void An_empty_knowledge_base_invites_the_first_article()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));

        var cut = RenderList();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No articles yet");
        cut.Find(".ts-state a.btn").GetAttribute("href").ShouldBe("/kb/new");
    }

    [Fact]
    public void A_filtered_empty_result_says_no_articles_match_and_clearing_goes_back_to_the_bare_list()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));
        var cut = RenderList("?status=Draft&search=zzz");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No articles match");

        cut.FindAll(".ts-state button").Single().Click();

        _navigation.Uri.ShouldEndWith("/kb");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "The API is unavailable."),
            TestData.Ok(TestData.KbPage([TestData.KbItem()])));

        var cut = RenderList();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the articles. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_failed_refresh_keeps_the_list_that_is_on_screen()
    {
        var cut = RenderList();
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "The API is unavailable."));

        cut.FindAll("button").Single(b => b.TextContent == "Refresh").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public void A_failed_lookup_only_empties_that_filter_and_the_list_still_loads()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error"));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<KbCategoryDto>>("api-error"));

        var cut = RenderList();

        cut.FindAll("tbody tr").Count.ShouldBe(2);
        cut.FindAll("#kb-product option").Count.ShouldBe(2);
        cut.FindAll("#kb-category option").Count.ShouldBe(1);
    }

    // Review Focus 4: a slow answer that was overtaken by a newer filter must not replace the newer list.
    [Fact]
    public void A_load_that_was_overtaken_by_a_newer_filter_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<KbArticleListItemDto>>>();
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == null), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == "Draft"), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Newer", "newer", KbArticleStatuses.Draft)])));
        var cut = RenderList();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        _navigation.NavigateTo("/kb?status=Draft");
        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbPage([TestData.KbItem("Older", "older")])));

        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));
        cut.Markup.ShouldNotContain("Older");
    }

    [Fact]
    public void A_filter_change_that_arrives_while_the_lookups_are_still_loading_is_loaded_once_and_remembered_as_that_filter()
    {
        var lookups = new TaskCompletionSource<Result<IReadOnlyList<ProductDto>>>();
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => lookups.Task);
        var cut = RenderList();

        _navigation.NavigateTo("/kb?status=Draft");
        cut.WaitForAssertion(() => Requests().Select(r => r.Status).ShouldBe(["Draft"]));
        lookups.SetResult(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly")]));

        // The first parameter set resumes with its own (older) filter: it must not load again, and must not leave the page believing it already loaded the unfiltered list.
        cut.WaitForAssertion(() => cut.FindAll("#kb-product option").Count.ShouldBe(3));
        Requests().Select(r => r.Status).ShouldBe(["Draft"]);
        _navigation.NavigateTo("/kb");
        cut.WaitForAssertion(() => Requests().Select(r => r.Status).ShouldBe(["Draft", null]));
    }

    [Fact]
    public void A_load_that_was_overtaken_and_then_fails_with_an_exception_is_swallowed_and_the_newer_list_stays()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<KbArticleListItemDto>>>();
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == null), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == "Draft"), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Newer", "newer", KbArticleStatuses.Draft)])));
        var cut = RenderList();
        _navigation.NavigateTo("/kb?status=Draft");
        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));

        slow.SetException(new InvalidOperationException("the old answer broke"));

        // The fault must not escape into the render (bUnit rethrows an unhandled lifecycle exception on the next wait) and says nothing on the newer list.
        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("the old answer broke");
    }

    [Fact]
    public void The_page_links_to_the_new_article_form_and_the_categories_page()
    {
        var cut = RenderList();

        cut.Find("a[href='/kb/new']").TextContent.ShouldBe("New article");
        cut.Find("a[href='/kb/categories']").TextContent.ShouldBe("Categories");
    }
}
