using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

public sealed class PublicProductClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Product() => new("paperplane", "Paperplane", "https://cdn.example.com/logo.png", "#F59E0B", "#000000", "#9D6507");

    [Fact]
    public async Task A_known_product_is_returned_with_its_branding()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());

        var result = await api.Get<IPublicProductClient>().GetAsync("paperplane", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Product());
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task The_product_list_is_a_read_of_the_list_route_with_the_key_and_display_name_only()
    {
        using var api = ApiHarness.Create();
        PublicProductSummaryDto[] list = [new("acme", "Acme"), new("paperplane", "Paperplane")];
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", list);

        var result = await api.Get<IPublicProductClient>().ListAsync(Ct);

        result.Value.ShouldBe(list);
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Path.ShouldBe("/api/public/products");
        sent.TicketToken.ShouldBeNull();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_failing_product_list_is_unavailable_after_the_read_retries()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products", HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<IPublicProductClient>().ListAsync(Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
    }

    [Fact]
    public async Task An_unknown_or_inactive_product_is_the_uniform_not_found_error()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var result = await api.Get<IPublicProductClient>().GetAsync("gone", Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Paperplane")]
    [InlineData("paper plane")]
    [InlineData("../admin")]
    [InlineData("paperplane/tickets")]
    [InlineData("paperplane?x=1")]
    [InlineData("paperplane#top")]
    [InlineData("%2e%2e")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());

        var result = await api.Get<IPublicProductClient>().GetAsync(key!, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    public async Task A_failure_of_the_api_is_a_result_with_its_own_code(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", status);

        var result = await api.Get<IPublicProductClient>().GetAsync("paperplane", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
    }
}
