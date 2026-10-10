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

        public Func<Result<PublicSiteDto>> Answer { get; set; } = () => Result<PublicSiteDto>.Success(new PublicSiteDto("slate"));

        public Task<Result<PublicSiteDto>> GetAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Reads);
            return Task.FromResult(Answer());
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

    [Fact]
    public async Task A_pack_the_portal_does_not_know_is_classic()
    {
        var (provider, client, _) = Build();
        client.Answer = () => Result<PublicSiteDto>.Success(new PublicSiteDto("neon;}"));

        (await provider.GetAsync(Ct)).ShouldBe("classic");
    }
}
