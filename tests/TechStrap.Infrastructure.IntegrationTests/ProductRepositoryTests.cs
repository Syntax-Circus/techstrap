using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class ProductRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task CommitAsync(AsyncServiceScope scope, Action<IProductRepository> stage)
    {
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        stage(scope.ServiceProvider.GetRequiredService<IProductRepository>());
        (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_saved_product_is_found_by_id_and_by_key_with_all_fields()
    {
        await using var host = new PersistenceTestHost(Database);
        var branding = ProductBranding.Create("Orbitly Cloud", "https://cdn.orbitly.example/logo.svg", "#112233", "help@orbitly.example", "reply@orbitly.example").Value;
        var product = Product.Create("orbitly", "Orbitly", "ORB", branding, host.Clock).Value;
        await using (var scope = host.CreateScope())
        {
            await CommitAsync(scope, products => products.Add(product));
        }

        await using var verify = host.CreateScope();
        var repository = verify.ServiceProvider.GetRequiredService<IProductRepository>();
        var byId = await repository.GetByIdAsync(product.Id, Ct);
        var byKey = await repository.GetByKeyAsync("orbitly", Ct);

        byId.ShouldNotBeNull();
        byKey.ShouldNotBeNull();
        byKey.Id.ShouldBe(product.Id);
        byKey.NumberPrefix.ShouldBe("ORB");
        byKey.Branding.ShouldBe(branding);
        byKey.IsActive.ShouldBeTrue();
        (await repository.GetByKeyAsync("missing", Ct)).ShouldBeNull();
        (await repository.GetByIdAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact(Timeout = 60_000)]
    public async Task GetExistingIdsAsync_returns_only_the_ids_that_exist_and_nothing_for_an_empty_list()
    {
        await using var host = new PersistenceTestHost(Database);
        var alpha = Product.Create("alpha", "Alpha", "ALP", null, host.Clock).Value;
        var beta = Product.Create("beta", "Beta", "BET", null, host.Clock).Value;
        await using (var scope = host.CreateScope())
        {
            await CommitAsync(scope, products =>
            {
                products.Add(alpha);
                products.Add(beta);
            });
        }

        await using var verify = host.CreateScope();
        var repository = verify.ServiceProvider.GetRequiredService<IProductRepository>();
        var missing = Guid.CreateVersion7();

        var existing = await repository.GetExistingIdsAsync([alpha.Id, missing, beta.Id], TestContext.Current.CancellationToken);

        existing.ShouldBe([alpha.Id, beta.Id], ignoreOrder: true);
        (await repository.GetExistingIdsAsync([], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await repository.GetExistingIdsAsync([missing], TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Listing_is_ordered_by_name_and_can_hide_inactive_products()
    {
        await using var host = new PersistenceTestHost(Database);
        var zeta = Product.Create("zeta", "Zeta", "ZET", null, host.Clock).Value;
        var alpha = Product.Create("alpha", "Alpha", "ALP", null, host.Clock).Value;
        var retired = Product.Create("retired", "Beta", "RET", null, host.Clock).Value;
        retired.SetActive(false);
        await using (var scope = host.CreateScope())
        {
            await CommitAsync(scope, products =>
            {
                products.Add(zeta);
                products.Add(alpha);
                products.Add(retired);
            });
        }

        await using var verify = host.CreateScope();
        var repository = verify.ServiceProvider.GetRequiredService<IProductRepository>();

        (await repository.ListAsync(false, Ct)).Select(p => p.Key).ShouldBe(["alpha", "retired", "zeta"]);
        (await repository.ListAsync(true, Ct)).Select(p => p.Key).ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public async Task Updating_a_loaded_product_persists_name_branding_and_active_flag_but_not_the_key_or_prefix()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
        await using (var scope = host.CreateScope())
        {
            await CommitAsync(scope, products => products.Add(product));
        }

        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var loaded = (await repository.GetByKeyAsync("orbitly", Ct))!;
            loaded.UpdateDetails("Orbitly 2", ProductBranding.Create("Orbitly 2", null, "#AA00FF", null, null).Value);
            loaded.SetActive(false);
            await CommitAsync(scope, products => products.Update(loaded));
        }

        await using var verify = host.CreateScope();
        var reloaded = (await verify.ServiceProvider.GetRequiredService<IProductRepository>().GetByKeyAsync("orbitly", Ct))!;
        reloaded.Name.ShouldBe("Orbitly 2");
        reloaded.Branding.AccentColour.ShouldBe("#AA00FF");
        reloaded.IsActive.ShouldBeFalse();
        reloaded.NumberPrefix.ShouldBe("ORB");
    }

    [Fact]
    public async Task Updating_a_product_that_was_not_loaded_in_the_scope_is_a_programming_error()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
        await using var scope = host.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IProductRepository>().Update(product))
            .Message.ShouldContain("was not loaded");
    }

    [Fact]
    public async Task Api_keys_are_found_by_hash_and_listed_newest_first_including_revoked()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
        var older = ProductApiKey.Create(product.Id, ApiKeyKind.Trusted, "tsk_aaaa", "hash-older", "Server", host.Clock).Value;
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = ProductApiKey.Create(product.Id, ApiKeyKind.Public, "tsk_bbbb", "hash-newer", "App", host.Clock).Value;
        await using (var scope = host.CreateScope())
        {
            await CommitAsync(scope, products =>
            {
                products.Add(product);
                products.AddApiKey(older);
                products.AddApiKey(newer);
            });
        }

        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var loaded = (await repository.GetApiKeyByHashAsync("hash-older", Ct))!;
            loaded.Kind.ShouldBe(ApiKeyKind.Trusted);
            loaded.Revoke(host.Clock);
            await CommitAsync(scope, products => products.UpdateApiKey(loaded));
        }

        await using var verify = host.CreateScope();
        var verifyRepository = verify.ServiceProvider.GetRequiredService<IProductRepository>();
        var keys = await verifyRepository.ListApiKeysAsync(product.Id, Ct);

        keys.Select(k => k.KeyPrefix).ShouldBe(["tsk_bbbb", "tsk_aaaa"]);
        keys[1].IsRevoked.ShouldBeTrue();
        (await verifyRepository.GetApiKeyAsync(newer.Id, Ct))!.Kind.ShouldBe(ApiKeyKind.Public);
        (await verifyRepository.GetApiKeyByHashAsync("nope", Ct)).ShouldBeNull();
    }
}
