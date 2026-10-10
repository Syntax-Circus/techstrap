using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>P09-T02, the KB search call behind the contact page's suggestions (09b); 09c extends the client. A read: retried, anonymous, forwarding the visitor's address.</summary>
public sealed class PublicKbClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/api/public/kb/paperplane/search";

    private static PagedResponse<PublicKbSearchResultDto> Page() =>
        new([new PublicKbSearchResultDto("reset-password", "Reset your password", "Use the reset link.", "accounts", "Accounts", "paperplane")], 1, 5, 1);

    [Fact]
    public async Task A_search_is_a_read_with_the_text_and_page_size_in_the_query_and_the_visitors_address()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        var result = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset password", 5, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Page(), new PagedComparer());
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBe("?q=reset%20password&pageSize=5");
        sent.TicketToken.ShouldBeNull();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task The_visitors_text_is_escaped_so_it_cannot_add_a_parameter()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "a&category=secret#x", 5, Ct);

        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?q=a%26category%3Dsecret%23x&pageSize=5");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paperplane")]
    [InlineData("../admin")]
    [InlineData("paperplane/kb")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        var result = await api.Get<IPublicKbClient>().SearchAsync(key!, "x", 5, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_429_is_rate_limited_and_a_503_is_retried_then_unavailable()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.TooManyRequests);
        var limited = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "x", 5, Ct);
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.ServiceUnavailable);
        var down = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "x", 5, Ct);

        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
        down.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, Path).ShouldBe(1 + 1 + ApiClientRegistration.ReadRetryCount);
    }

    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_search_for_a_page_sends_the_page_and_the_old_overload_still_sends_none()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset", 3, 10, Ct);
        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset", 10, Ct);

        api.Stub.Requests.Select(request => request.Query).ShouldBe(["?q=reset&page=3&pageSize=10", "?q=reset&pageSize=10"]);
        api.Stub.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read && request.TicketToken == null);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task The_categories_are_a_read_of_the_categories_route_and_keep_their_counts()
    {
        using var api = ApiHarness.Create();
        PublicKbCategoryDto[] categories = [new("accounts", "Accounts", "Sign-in and passwords", 4), new("billing", "Billing", null, 1)];
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/categories", categories);

        var result = await api.Get<IPublicKbClient>().ListCategoriesAsync("paperplane", Ct);

        result.Value.ShouldBe(categories);
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_category_page_is_a_read_with_the_page_and_the_page_size_in_the_query()
    {
        using var api = ApiHarness.Create();
        var page = new PagedResponse<PublicKbArticleSummaryDto>([new("reset-password", "Reset your password", "How to reset", "accounts", "Accounts", "paperplane", Updated)], 2, 10, 11);
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/categories/accounts/articles", page);

        var result = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync("paperplane", "accounts", 2, 10, Ct);

        result.Value.Items.ShouldBe(page.Items);
        (result.Value.Page, result.Value.PageSize, result.Value.TotalCount).ShouldBe((2, 10, 11));
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Query.ShouldBe("?page=2&pageSize=10");
        sent.Client.ShouldBe(ApiClientNames.Read);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task An_article_is_a_read_of_its_own_route_and_its_html_comes_back_untouched()
    {
        using var api = ApiHarness.Create();
        var article = new PublishedKbArticleDto("paperplane", "accounts", "Accounts", "reset-password", "Reset your password", "How to reset", "<h2>Steps</h2>\n<p>Open <a href=\"https://x.test\">settings</a>.</p>", Updated, Updated);
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/reset-password", article);

        var result = await api.Get<IPublicKbClient>().GetArticleAsync("paperplane", "accounts", "reset-password", Ct);

        result.Value.ShouldBe(article);
        result.Value.Html.ShouldBe(article.Html, "the Portal never rewrites what the API sanitized");
        api.Stub.Requests.ShouldHaveSingleItem().Client.ShouldBe(ApiClientNames.Read);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task The_sitemap_entries_are_a_read_and_a_shared_article_has_no_product_key()
    {
        using var api = ApiHarness.Create();
        KbSitemapEntryDto[] entries = [new(null, "general", "shared-tips", Updated), new("paperplane", "accounts", "reset-password", Updated)];
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", entries);

        var result = await api.Get<IPublicKbClient>().GetSitemapAsync("paperplane", Ct);

        result.Value.ShouldBe(entries);
        api.Stub.Requests.ShouldHaveSingleItem().Client.ShouldBe(ApiClientNames.Read);
    }

    [Theory]
    [InlineData(null, "accounts", "reset")]
    [InlineData("", "accounts", "reset")]
    [InlineData("Paperplane", "accounts", "reset")]
    [InlineData("../admin", "accounts", "reset")]
    [InlineData("paperplane", null, "reset")]
    [InlineData("paperplane", "", "reset")]
    [InlineData("paperplane", "Accounts", "reset")]
    [InlineData("paperplane", "a/b", "reset")]
    [InlineData("paperplane", "a?b", "reset")]
    [InlineData("paperplane", "a#b", "reset")]
    [InlineData("paperplane", "..", "reset")]
    [InlineData("paperplane", "accounts", null)]
    [InlineData("paperplane", "accounts", "")]
    [InlineData("paperplane", "accounts", "Reset")]
    [InlineData("paperplane", "accounts", "re set")]
    [InlineData("paperplane", "accounts", "re%2fset")]
    [InlineData("paperplane", "accounts", "reset-")]
    [InlineData("paperplane", "accounts", "-reset")]
    [InlineData("paperplane", "accounts", "re--set")]
    public async Task An_article_with_a_key_or_slug_that_is_not_a_slug_is_the_uniform_not_found_and_no_call_is_made(string? product, string? category, string? slug)
    {
        using var api = ApiHarness.Create();

        var article = await api.Get<IPublicKbClient>().GetArticleAsync(product!, category!, slug!, Ct);

        article.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "accounts")]
    [InlineData("", "accounts")]
    [InlineData("Paperplane", "accounts")]
    [InlineData("paperplane", null)]
    [InlineData("paperplane", "")]
    [InlineData("paperplane", "Accounts")]
    [InlineData("paperplane", "a/b")]
    [InlineData("paperplane", "a?page=9")]
    [InlineData("paperplane", "a#b")]
    [InlineData("paperplane", "..")]
    [InlineData("paperplane", "accounts-")]
    public async Task A_category_page_with_a_key_or_slug_that_is_not_a_slug_is_the_uniform_not_found_and_no_call_is_made(string? product, string? category)
    {
        using var api = ApiHarness.Create();

        var page = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync(product!, category!, 1, 10, Ct);

        page.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paperplane")]
    [InlineData("../admin")]
    [InlineData("paperplane/kb")]
    public async Task The_categories_the_sitemap_and_a_paged_search_refuse_a_key_that_is_not_a_slug_without_a_call(string? product)
    {
        using var api = ApiHarness.Create();
        var expected = new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound);

        (await api.Get<IPublicKbClient>().ListCategoriesAsync(product!, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
        (await api.Get<IPublicKbClient>().GetSitemapAsync(product!, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
        (await api.Get<IPublicKbClient>().SearchAsync(product!, "x", 1, 10, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_404_is_the_uniform_not_found_a_429_is_rate_limited_and_a_503_is_retried_then_unavailable_on_every_new_read()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/gone", HttpStatusCode.NotFound, "kb-article-not-found", "That article does not exist.");
        api.Stub.OnProblem(HttpMethod.Get, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound, "kb-category-not-found", "That category does not exist.");
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/paperplane/categories", HttpStatusCode.TooManyRequests);
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", HttpStatusCode.ServiceUnavailable);

        var article = await api.Get<IPublicKbClient>().GetArticleAsync("paperplane", "accounts", "gone", Ct);
        var category = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync("paperplane", "gone", 1, 10, Ct);
        var limited = await api.Get<IPublicKbClient>().ListCategoriesAsync("paperplane", Ct);
        var down = await api.Get<IPublicKbClient>().GetSitemapAsync("paperplane", Ct);

        article.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        category.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
        down.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/public/kb/paperplane/sitemap").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
    }

    private sealed class PagedComparer : IEqualityComparer<PagedResponse<PublicKbSearchResultDto>>
    {
        public bool Equals(PagedResponse<PublicKbSearchResultDto>? x, PagedResponse<PublicKbSearchResultDto>? y) =>
            x is not null && y is not null && x.Page == y.Page && x.PageSize == y.PageSize && x.TotalCount == y.TotalCount && x.Items.SequenceEqual(y.Items);

        public int GetHashCode(PagedResponse<PublicKbSearchResultDto> obj) => obj.TotalCount;
    }
}
