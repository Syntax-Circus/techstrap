using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;

namespace TechStrap.Portal.Tests.Hosting;

/// <summary>P11e-T05: the host map. A host resolves to its product key (lower-case compare); a miss costs at most one API call in ten seconds; the map is read again after a minute; a failed read keeps the last map.</summary>
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
            Map = new ProductHostMap(Provider.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ProductHostMap>.Instance);
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
    }

    [Fact(Timeout = 30_000)]
    public async Task A_known_host_resolves_to_its_product_key_in_any_case()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());

        (await fixture.Map.FindKeyAsync("Support.DragonPoop.com", refreshOnMiss: true, ct)).ShouldBe("dragon-poop");

        fixture.Map.TryResolve("support.dragonpoop.com", out var key).ShouldBeTrue();
        key.ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task An_unknown_host_is_not_a_product_host_and_the_miss_is_one_api_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());

        (await fixture.Map.FindKeyAsync("evil.example", refreshOnMiss: true, ct)).ShouldBeNull();

        fixture.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_second_miss_inside_ten_seconds_makes_no_second_call_and_one_after_ten_seconds_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("evil.example", refreshOnMiss: true, ct);

        fixture.Clock.Advance(ProductHostMapOptions.MissRefreshInterval - TimeSpan.FromSeconds(1));
        await fixture.Map.FindKeyAsync("other.example", refreshOnMiss: true, ct);
        fixture.Calls.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Map.FindKeyAsync("other.example", refreshOnMiss: true, ct);
        fixture.Calls.ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_host_added_since_the_last_read_is_found_by_the_miss_refresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo"));
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct);
        fixture.Set(Products());
        fixture.Clock.Advance(ProductHostMapOptions.MissRefreshInterval);

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBe("dragon-poop");
    }

    [Fact(Timeout = 30_000)]
    public async Task The_map_is_read_again_after_the_sixty_second_lifetime_and_not_before()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct);
        fixture.Set(new PublicProductSummaryDto("dragon-poop", "Dragon Poop", "help.dragonpoop.com"));

        fixture.Clock.Advance(ProductHostMapOptions.Ttl - TimeSpan.FromSeconds(1));
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBe("dragon-poop");
        fixture.Calls.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.Map.FindKeyAsync("help.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBe("dragon-poop");
        fixture.Map.TryResolve("support.dragonpoop.com", out _).ShouldBeFalse();
        fixture.Calls.ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_product_host_is_found_by_its_key_and_a_product_without_one_is_not()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.EnsureFreshAsync(ct);

        fixture.Map.TryGetHost("dragon-poop", out var host).ShouldBeTrue();
        host.ShouldBe("support.dragonpoop.com");
        fixture.Map.TryGetHost("who-flung-poo", out _).ShouldBeFalse();
        fixture.Map.TryGetHost("nobody", out _).ShouldBeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failed_read_keeps_the_previous_map_and_throws_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct);
        fixture.Fail();
        fixture.Clock.Advance(ProductHostMapOptions.Ttl);

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBe("dragon-poop");

        fixture.Calls.ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failed_first_read_gives_an_empty_map_and_is_not_retried_inside_ten_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new Fixture(Products());
        fixture.Fail();

        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBeNull();
        (await fixture.Map.FindKeyAsync("support.dragonpoop.com", refreshOnMiss: true, ct)).ShouldBeNull();

        fixture.Calls.ShouldBe(1);
    }
}
