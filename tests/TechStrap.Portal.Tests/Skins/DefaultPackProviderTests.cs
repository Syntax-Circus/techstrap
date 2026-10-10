using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SyntaxCircus.Common;
using TechStrap.Contracts.Settings;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Skins;

/// <summary>The default pack is read through a one-minute snapshot: it never costs a request a call once it is known, serves the last good value while it refreshes, and is "classic" when nothing was ever read.</summary>
public sealed class DefaultPackProviderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class FakeClient : ISiteSettingsClient
    {
        public int Reads;

        /// <summary>Completed when a read has reached the client, which is after the provider built the read's deadline: the point from which the fake clock may be advanced.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<Result<PublicSiteDto>> Answer { get; set; } = () => Result<PublicSiteDto>.Success(new PublicSiteDto("slate"));

        /// <summary>When set, a read never answers by itself: it ends only when the caller's token is cancelled (a hung API).</summary>
        public bool Hang { get; set; }

        public async Task<Result<PublicSiteDto>> GetAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Reads);
            Entered.TrySetResult();
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Answer();
        }
    }

    private static (DefaultPackProvider Provider, FakeClient Client, FakeTimeProvider Clock) Build()
    {
        var client = new FakeClient();
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection().AddScoped<ISiteSettingsClient>(_ => client).BuildServiceProvider();
        return (new DefaultPackProvider(services.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<DefaultPackProvider>.Instance), client, clock);
    }

    [Fact]
    public async Task The_first_call_reads_once_and_a_second_call_inside_a_minute_reads_nothing()
    {
        var (provider, client, clock) = Build();

        (await provider.GetAsync(Ct)).ShouldBe("slate");
        clock.Advance(TimeSpan.FromSeconds(59));
        (await provider.GetAsync(Ct)).ShouldBe("slate");

        client.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task After_a_minute_the_stale_value_is_returned_at_once_and_refreshed_once_in_the_background()
    {
        var (provider, client, clock) = Build();
        await provider.GetAsync(Ct);
        client.Answer = () => Result<PublicSiteDto>.Success(new PublicSiteDto("midnight"));
        clock.Advance(TimeSpan.FromSeconds(61));

        (await provider.GetAsync(Ct)).ShouldBe("slate", "the stale value is answered without waiting");
        await provider.PendingRefresh;

        client.Reads.ShouldBe(2);
        (await provider.GetAsync(Ct)).ShouldBe("midnight");
        client.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_good_value()
    {
        var (provider, client, clock) = Build();
        await provider.GetAsync(Ct);
        client.Answer = () => throw new HttpRequestException("down");
        clock.Advance(TimeSpan.FromSeconds(61));

        await provider.GetAsync(Ct);
        await provider.PendingRefresh;

        (await provider.GetAsync(Ct)).ShouldBe("slate");
    }

    [Fact]
    public async Task A_cold_failure_is_classic()
    {
        var (provider, client, _) = Build();
        client.Answer = () => Result<PublicSiteDto>.Failure(ProblemMapping.Unavailable());

        (await provider.GetAsync(Ct)).ShouldBe("classic");
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact(Timeout = 60_000)]
    public async Task A_hung_cold_read_costs_the_first_caller_two_seconds_at_most_and_the_next_none()
    {
        var (provider, client, clock) = Build();
        client.Hang = true;

        var first = provider.GetAsync(TestContext.Current.CancellationToken).AsTask();

        // The read runs as a task of its own: it has built its deadline once it is inside the client. Advancing before that would leave the deadline unelapsed forever.
        await client.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        clock.Advance(DefaultPackProvider.ColdWait + TimeSpan.FromMilliseconds(1));
        (await first.WaitAsync(Bound, TestContext.Current.CancellationToken)).ShouldBe("classic", "the first caller stopped waiting at the bound");

        var second = provider.GetAsync(TestContext.Current.CancellationToken);
        second.IsCompletedSuccessfully.ShouldBeTrue("a second request does not wait for the hung read");
        (await second).ShouldBe("classic");
        client.Reads.ShouldBe(1, "and does not start another read while one is running");

        // The hung read is cut off by its own deadline; the next read (after the retry interval) succeeds and replaces Classic.
        clock.Advance(TimeSpan.FromSeconds(31));
        await provider.PendingRefresh.WaitAsync(Bound, TestContext.Current.CancellationToken);
        client.Hang = false;
        clock.Advance(TimeSpan.FromSeconds(11));
        (await provider.GetAsync(TestContext.Current.CancellationToken)).ShouldBe("classic", "the stale Classic is answered at once while the refresh runs");
        await provider.PendingRefresh.WaitAsync(Bound, TestContext.Current.CancellationToken);

        (await provider.GetAsync(TestContext.Current.CancellationToken)).ShouldBe("slate");
    }

    [Fact]
    public async Task A_pack_the_portal_does_not_know_is_classic()
    {
        var (provider, client, _) = Build();
        client.Answer = () => Result<PublicSiteDto>.Success(new PublicSiteDto("neon;}"));

        (await provider.GetAsync(Ct)).ShouldBe("classic");
    }
}
