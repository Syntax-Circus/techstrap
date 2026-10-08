using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Client.Tests.Infrastructure;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Submit;

public sealed partial class SubmitRetryTests
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Hex32();

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_retryable_status_is_retried_with_the_identical_key(HttpStatusCode status)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status).Respond(status).Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        fixture.Stub.Requests.Count.ShouldBe(3);
        var keys = fixture.Stub.Requests.Select(r => r.Header(HeaderNames.IdempotencyKey)).ToList();
        keys[0].ShouldNotBeNull();
        keys.Distinct().Count().ShouldBe(1);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_non_retryable_status_is_sent_once(HttpStatusCode status)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status, "{}", "application/problem+json").Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        fixture.Stub.Requests.Count.ShouldBe(1);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_transport_failure_is_retried_then_reported_after_max_attempts()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 4);
        fixture.Stub.Throw(() => new HttpRequestException("down"), times: 4).Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        fixture.Stub.Requests.Count.ShouldBe(4);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_transport_failure_that_clears_up_succeeds()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Throw(() => new HttpRequestException("down")).Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        fixture.Stub.Requests.Count.ShouldBe(2);
    }

    [Fact(Timeout = 10_000)]
    public async Task MaxAttempts_1_never_retries()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 1);
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable).Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        fixture.Stub.Requests.Count.ShouldBe(1);
    }

    [Fact(Timeout = 10_000)]
    public async Task MaxAttempts_is_the_total_number_of_sends()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 2);
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable, times: 5);

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        fixture.Stub.Requests.Count.ShouldBe(2);
    }

    [Fact(Timeout = 10_000)]
    public async Task SubmitTicketOnce_sends_no_key_and_makes_one_attempt()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable).Created();

        var result = await fixture.Client.SubmitTicketOnceAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        var sent = fixture.Stub.Requests.ShouldHaveSingleItem();
        sent.Headers.ContainsKey(HeaderNames.IdempotencyKey).ShouldBeFalse();
    }

    [Fact(Timeout = 10_000)]
    public async Task SubmitTicketOnce_does_not_retry_a_transport_failure()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Throw(() => new HttpRequestException("down")).Created();

        var result = await fixture.Client.SubmitTicketOnceAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        fixture.Stub.Requests.Count.ShouldBe(1);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_generated_key_is_32_hex_and_differs_per_call_but_not_per_attempt()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable).Created().Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);
        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        var keys = fixture.Stub.Requests.Select(r => r.Header(HeaderNames.IdempotencyKey)!).ToList();
        keys.Count.ShouldBe(3);
        keys.ShouldAllBe(k => Hex32().IsMatch(k));
        keys[0].ShouldBe(keys[1]);
        keys[2].ShouldNotBe(keys[0]);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_supplied_key_is_sent_verbatim_on_every_attempt()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadGateway).Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), "order-42_retry.1", Xunit.TestContext.Current.CancellationToken);

        fixture.Stub.Requests.ShouldAllBe(r => r.Header(HeaderNames.IdempotencyKey) == "order-42_retry.1");
        fixture.Stub.Requests.Count.ShouldBe(2);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_key_of_the_maximum_length_is_accepted()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();

        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), new string('k', IntakeLimits.MaxIdempotencyKeyLength), Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory(Timeout = 10_000)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("tab\there")]
    [InlineData("café")]
    [InlineData("line\nbreak")]
    public async Task An_invalid_key_throws_before_anything_is_sent(string key)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();

        await Should.ThrowAsync<ArgumentException>(() => fixture.Client.SubmitTicketAsync(ClientFixture.Request(), key, Xunit.TestContext.Current.CancellationToken));

        fixture.Stub.Requests.ShouldBeEmpty();
    }

    [Fact(Timeout = 10_000)]
    public async Task A_key_longer_than_the_limit_throws_before_anything_is_sent()
    {
        using var fixture = new ClientFixture();

        await Should.ThrowAsync<ArgumentException>(() =>
            fixture.Client.SubmitTicketAsync(ClientFixture.Request(), new string('k', IntakeLimits.MaxIdempotencyKeyLength + 1), Xunit.TestContext.Current.CancellationToken));

        fixture.Stub.Requests.ShouldBeEmpty();
    }

    [Fact(Timeout = 10_000)]
    public async Task Each_attempt_gets_a_fresh_request_and_body()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable, times: 2).Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        var sent = fixture.Stub.Requests;
        sent.Count.ShouldBe(3);
        sent.Select(r => r.Message).Distinct().Count().ShouldBe(3);
        sent.Select(r => r.Content).Distinct().Count().ShouldBe(3);
        sent[0].Body.ShouldNotBeNullOrEmpty();
        sent.ShouldAllBe(r => r.Body == sent[0].Body);
    }
}
