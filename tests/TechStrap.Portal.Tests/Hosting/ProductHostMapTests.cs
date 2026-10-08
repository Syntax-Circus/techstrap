using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Hosting;

/// <summary>
/// P11e-T05: the host map. A host resolves to its product key (any case); a miss costs at most one API call in ten seconds; an expired map is served as it is while one background read renews it; a failed read keeps
/// the last map; a caller that gives up never cancels the shared read.
/// </summary>
public sealed class ProductHostMapTests
{
    private static PublicProductSummaryDto[] Products() =>
    [
        new("dragon-poop", "Dragon Poop", "support.dragonpoop.com"),
        new("who-flung-poo", "Who Flung Poo"),
    ];

    private sealed class Fixture
    {
        public Fixture(params PublicProductSummaryDto[] products)
        {
            Client = Substitute.For<IPublicProductClient>();
            Set(products);
            var services = new ServiceCollection();
            services.AddScoped(_ => Client);
            Provider = services.BuildServiceProvider();
            Clock = new FakeTimeProvider();
            Map = new ProductHostMap(Provider.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ProductHostMap>.Instance, new HttpContextAccessor(), Options.Create(new PortalOptions { PublicUrl = "https://portal.test" }));
        }

        public IPublicProductClient Client { get; }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public ProductHostMap Map { get; }

        public int Calls => Client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPublicProductClient.ListAsync));

        public void Set(params PublicProductSummaryDto[] products) =>
            Client.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<IReadOnlyList<PublicProductSummaryDto>>.Success(products)));

        public void Fail() =>
            Client.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<IReadOnlyList<PublicProductSummaryDto>>.Failure(ProblemMapping.Unexpected())));

        /// <summary>Makes the next read wait until the returned source is completed.</summary>
        public TaskCompletionSource<Result<IReadOnlyList<PublicProductSummaryDto>>> Hold(out Task started)
        {
            var gate = new TaskCompletionSource<Result<IReadOnlyList<PublicProductSummaryDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Client.ListAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                begun.TrySetResult();
                return gate.Task;
            });
            started = begun.Task;
            return gate;
        }
    }

    private static Result<IReadOnlyList<PublicProductSummaryDto>> Ok(params PublicProductSummaryDto[] products) => Result<IReadOnlyList<PublicProductSummaryDto>>.Success(products);

    [Fact(Timeout = 30_000)]
    public async Task A_known_host_resolves_to_its_product_key_in_any_case()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());

        (await fixture.Map.FindKeyAsync("Support.DragonPoop.com", ct)).ShouldBe("dragon-poop");

        fixture.Map.TryResolve("SUPPORT.dragonpoop.com", out var key).ShouldBeTrue();
        key.ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task An_unknown_host_is_not_a_product_host_and_the_miss_is_one_api_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());

        (await fixture.Map.FindKeyAsync("evil.example", ct)).ShouldBeNull();

        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_second_miss_inside_ten_seconds_makes_no_second_call_and_one_after_ten_seconds_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("evil.example", ct);

        fixture.Clock.Advance(ProductHostMapOptions.MissRefreshInterval - TimeSpan.FromSeconds(1));
        await fixture.Map.FindKeyAsync("other.example", ct);
        fixture.Calls.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Map.FindKeyAsync("other.example", ct);
        fixture.Calls.ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task Two_concurrent_misses_inside_the_ten_second_window_make_no_call_and_wait_for_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("evil.example", ct);

        var misses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(index => Task.Run(async () => await fixture.Map.FindKeyAsync($"miss{index}.example", ct), ct)));

        misses.ShouldAllBe(key => key == null);
        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_host_added_since_the_last_read_is_found_by_the_miss_refresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo"));
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct);
        fixture.Set(Products());
        fixture.Clock.Advance(ProductHostMapOptions.MissRefreshInterval);

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
    }

    [Fact(Timeout = 30_000)]
    public async Task An_expired_map_is_served_as_it_is_while_one_background_read_renews_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct);
        var reload = fixture.Hold(out var reloadStarted);

        fixture.Clock.Advance(ProductHostMapOptions.Ttl - TimeSpan.FromSeconds(1));
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop", "the old map answers while the read is pending");
        await reloadStarted;
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(2, "one read, however many requests arrive meanwhile");
        fixture.Map.TryResolve("help.dragonpoop.com", out _).ShouldBeFalse();

        reload.SetResult(Ok(new PublicProductSummaryDto("dragon-poop", "Dragon Poop", "help.dragonpoop.com")));
        await fixture.Map.PendingRefresh;

        (await fixture.Map.FindKeyAsync("help.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        fixture.Map.TryResolve("support.dragonpoop.com", out _).ShouldBeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_caller_that_cancels_its_own_token_does_not_cancel_the_shared_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        var load = fixture.Hold(out _);
        using var impatient = new CancellationTokenSource();

        var first = fixture.Map.FindKeyAsync("support.dragonpoop.com", impatient.Token).AsTask();
        await impatient.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        load.SetResult(Ok(Products()));
        await fixture.Map.PendingRefresh;

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_product_host_is_found_by_its_key_in_any_case_and_a_product_without_one_is_not()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.EnsureFreshAsync(ct);

        fixture.Map.TryGetHost("Dragon-Poop", out var host).ShouldBeTrue();
        host.ShouldBe("support.dragonpoop.com");
        fixture.Map.TryGetHost("who-flung-poo", out _).ShouldBeFalse();
        fixture.Map.TryGetHost("nobody", out _).ShouldBeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failed_read_keeps_the_previous_map_and_throws_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct);
        fixture.Fail();
        fixture.Clock.Advance(ProductHostMapOptions.Ttl);

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        await fixture.Map.PendingRefresh;

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failed_first_read_gives_an_empty_map_and_is_not_retried_inside_ten_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        fixture.Fail();

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBeNull();
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBeNull();

        fixture.Calls.ShouldBe(1);
    }

    // Review F6: the key and the host of one answer come from one snapshot, so a swap between two reads can never pair them.
    [Fact(Timeout = 30_000)]
    public async Task A_host_is_found_with_its_key_and_its_stored_host_in_one_answer()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());

        var found = await fixture.Map.FindAsync("Support.DragonPoop.com", ct);

        found.ShouldNotBeNull();
        found.Value.Key.ShouldBe("dragon-poop");
        found.Value.Host.ShouldBe("support.dragonpoop.com");
        (await fixture.Map.FindAsync("evil.example", ct)).ShouldBeNull();
    }

    // Review F2b: the default Portal host is never a product host, whatever the API lists.
    [Fact(Timeout = 30_000)]
    public async Task A_product_listed_with_the_default_host_is_skipped_and_logged_once_by_key_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var lines = new List<string>();
        var client = Substitute.For<IPublicProductClient>();
        client.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Ok(
            new PublicProductSummaryDto("dragon-poop", "Dragon Poop", "support.dragonpoop.com"),
            new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo", "Portal.Test"))));
        var services = new ServiceCollection();
        services.AddScoped(_ => client);
        var map = new ProductHostMap(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), new ListLogger(lines), new HttpContextAccessor(), Options.Create(new PortalOptions { PublicUrl = "https://portal.test/" }));

        await map.EnsureFreshAsync(ct);

        map.TryResolve("portal.test", out _).ShouldBeFalse();
        map.TryGetHost("who-flung-poo", out _).ShouldBeFalse();
        map.TryGetHost("dragon-poop", out var host).ShouldBeTrue();
        host.ShouldBe("support.dragonpoop.com");
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("who-flung-poo");
        lines[0].ShouldNotContain("portal.test", Case.Insensitive);
    }

    // Review F9: a read that never answers is cut off, the stale map keeps serving, and the next read can start.
    [Fact(Timeout = 30_000)]
    public async Task A_read_that_never_answers_is_cut_off_and_the_next_read_can_start()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct);
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.ListAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            begun.TrySetResult();
            return NeverAnswers(call.Arg<CancellationToken>());
        });
        fixture.Clock.Advance(ProductHostMapOptions.Ttl);

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", ct)).ShouldBe("dragon-poop", "the stale map answers while the read hangs");
        await begun.Task;
        fixture.Map.PendingRefresh.IsCompleted.ShouldBeFalse();

        fixture.Clock.Advance(ProductHostMapOptions.ReadTimeout);
        await fixture.Map.PendingRefresh;

        fixture.Map.TryResolve("support.dragonpoop.com", out _).ShouldBeTrue("the old map is kept");
        fixture.Set(new PublicProductSummaryDto("dragon-poop", "Dragon Poop", "help.dragonpoop.com"));
        await fixture.Map.EnsureFreshAsync(ct);
        await fixture.Map.PendingRefresh;

        fixture.Map.TryResolve("help.dragonpoop.com", out _).ShouldBeTrue("a new read started after the timeout");
    }

    private static async Task<Result<IReadOnlyList<PublicProductSummaryDto>>> NeverAnswers(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return Ok();
    }

    private sealed class ListLogger(List<string> lines) : ILogger<ProductHostMap>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Add(formatter(state, exception));
    }
}
