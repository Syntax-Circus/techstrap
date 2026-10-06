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

    private sealed class PagedComparer : IEqualityComparer<PagedResponse<PublicKbSearchResultDto>>
    {
        public bool Equals(PagedResponse<PublicKbSearchResultDto>? x, PagedResponse<PublicKbSearchResultDto>? y) =>
            x is not null && y is not null && x.Page == y.Page && x.PageSize == y.PageSize && x.TotalCount == y.TotalCount && x.Items.SequenceEqual(y.Items);

        public int GetHashCode(PagedResponse<PublicKbSearchResultDto> obj) => obj.TotalCount;
    }
}
