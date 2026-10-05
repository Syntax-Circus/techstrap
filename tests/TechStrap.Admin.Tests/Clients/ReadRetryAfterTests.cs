using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// The read client honours <c>Retry-After</c> but never waits more than <see cref="ApiClientRegistration.ReadRetryAfterCap"/> for it: an overloaded API that
/// asks for two minutes must not freeze a page on "Loading" until the client timeout.
/// </summary>
public sealed class ReadRetryAfterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

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
    public void A_delay_in_seconds_is_honoured_up_to_the_cap(int seconds, double expectedSeconds)
    {
        var delay = ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds))), Now);

        delay.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void A_date_is_converted_with_the_clock_and_capped_the_same_way()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMilliseconds(1500))), Now).ShouldBe(TimeSpan.FromMilliseconds(1500));
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(10))), Now).ShouldBe(ApiClientRegistration.ReadRetryAfterCap);
    }

    [Fact]
    public void No_header_a_zero_delay_or_a_date_in_the_past_leaves_the_backoff_in_charge()
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
        await using var api = await ApiHarness.CreateAsync(time: time);
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));

        var call = api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", TestContext.Current.CancellationToken);
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
        stepped.ShouldBeGreaterThanOrEqualTo(ApiClientRegistration.ReadRetryAfterCap, "the header is still honoured, not ignored");
        stepped.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(2 * ApiClientRegistration.ReadRetryCount + 1));
    }
}
