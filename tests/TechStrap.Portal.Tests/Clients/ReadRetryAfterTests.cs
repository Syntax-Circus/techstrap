using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// The read client honors <c>Retry-After</c> but never waits more than <see cref="ApiClientRegistration.ReadRetryAfterCap"/> for it: an overloaded API that says "120" must not freeze
/// a visitor's page on "loading" until the client timeout.
/// </summary>
public sealed class ReadRetryAfterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage WithRetryAfter(RetryConditionHeaderValue? value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = value;
        return response;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(120, 2)]
    public void A_retry_after_in_seconds_is_honoured_up_to_the_cap(int seconds, int expectedSeconds)
    {
        var delay = ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds))), Now);

        delay.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void A_retry_after_date_is_honoured_up_to_the_cap()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMilliseconds(1500))), Now).ShouldBe(TimeSpan.FromMilliseconds(1500));
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(10))), Now).ShouldBe(ApiClientRegistration.ReadRetryAfterCap);
    }

    [Fact]
    public void No_header_a_zero_wait_or_a_date_in_the_past_leaves_the_backoff_in_charge()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(null), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.Zero)), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(-5))), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(null, Now).ShouldBeNull();
    }

    [Fact(Timeout = 60000)]
    public async Task A_503_that_asks_for_two_minutes_is_retried_after_at_most_the_cap()
    {
        var time = new FakeTimeProvider(Now);
        using var api = ApiHarness.Create(time: time);
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));

        var call = api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", TestContext.Current.CancellationToken);
        var stepped = TimeSpan.Zero;
        while (!call.IsCompleted && stepped < TimeSpan.FromSeconds(30))
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            stepped += TimeSpan.FromMilliseconds(250);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var finishedInTime = call.IsCompleted;
        time.Advance(TimeSpan.FromMinutes(10)); // a failing run must still end, not wait on the fake clock for ever
        finishedInTime.ShouldBeTrue("the retries must not wait for the 120 seconds the API asked for");
        (await call).Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        stepped.ShouldBeGreaterThanOrEqualTo(ApiClientRegistration.ReadRetryAfterCap, "the header is still honored, not ignored");
        stepped.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(2 * ApiClientRegistration.ReadRetryCount + 1));
    }
}
