using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class UnitOfWorkTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private PersistenceTestHost Host() => new(Database);

    private static Product NewProduct(PersistenceTestHost host, string key = "acme", string prefix = "ACME") =>
        Product.Create(key, key, prefix, null, host.Clock).Value;

    [Fact]
    public async Task Commit_writes_everything_staged_in_the_scope_in_one_transaction()
    {
        await using var host = Host();
        var product = NewProduct(host);
        await using (var scope = host.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var work = await uow.BeginAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(product);
            scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().Add(
                AdminEvent.Record(AdminEventType.ProductCreated, Guid.NewGuid(), AdminSubjectType.Product, product.Id, null, host.Clock).Value);

            (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }

        await using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<IProductRepository>().GetByKeyAsync("acme", Ct)).ShouldNotBeNull();
        (await verify.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(null, 1, 10, Ct)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Leaving_the_scope_without_committing_rolls_back_everything_staged()
    {
        await using var host = Host();
        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(NewProduct(host));
        }

        await using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<IProductRepository>().ListAsync(false, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_exception_before_commit_leaves_no_product_and_no_admin_event_from_that_scope()
    {
        await using var host = Host();
        var product = NewProduct(host);
        var failBeforeCommit = async () =>
        {
            await using var scope = host.CreateScope();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(product);
            scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().Add(
                AdminEvent.Record(AdminEventType.ProductCreated, Guid.NewGuid(), AdminSubjectType.Product, product.Id, null, host.Clock).Value);
            throw new InvalidOperationException("boom before commit");
        };

        await Should.ThrowAsync<InvalidOperationException>(failBeforeCommit);

        await using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<IProductRepository>().ListAsync(false, Ct)).ShouldBeEmpty();
        (await verify.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(null, 1, 10, Ct)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Two_stale_updates_give_one_success_and_one_conflict_result_never_an_exception()
    {
        await using var host = Host();
        await SeedAsync(host, NewProduct(host));

        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstProducts = first.ServiceProvider.GetRequiredService<IProductRepository>();
        var secondProducts = second.ServiceProvider.GetRequiredService<IProductRepository>();
        var loadedByFirst = (await firstProducts.GetByKeyAsync("acme", Ct))!;
        var loadedBySecond = (await secondProducts.GetByKeyAsync("acme", Ct))!;
        loadedByFirst.UpdateDetails("Renamed by first", loadedByFirst.Branding);
        loadedBySecond.UpdateDetails("Renamed by second", loadedBySecond.Branding);

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstProducts.Update(loadedByFirst);
        var firstResult = await firstWork.CommitAsync(Ct);

        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondProducts.Update(loadedBySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.IsSuccess.ShouldBeFalse();
        var error = secondResult.Errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Conflict);
        error.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);

        await using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<IProductRepository>().GetByKeyAsync("acme", Ct))!.Name.ShouldBe("Renamed by first");
    }

    [Fact]
    public async Task A_duplicate_unique_value_is_a_conflict_result_and_the_whole_scope_is_rolled_back()
    {
        await using var host = Host();
        await SeedAsync(host, NewProduct(host));

        await using var scope = host.CreateScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var duplicate = NewProduct(host, "acme", "OTHER");
        scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(duplicate);
        scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().Add(
            AdminEvent.Record(AdminEventType.ProductCreated, Guid.NewGuid(), AdminSubjectType.Product, duplicate.Id, null, host.Clock).Value);

        var result = await work.CommitAsync(Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        await using var verify = host.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(null, 1, 10, Ct)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task After_a_conflict_the_same_context_can_run_a_fresh_scope()
    {
        await using var host = Host();
        await SeedAsync(host, NewProduct(host));
        await using var scope = host.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        await using (var failing = await uow.BeginAsync(Ct))
        {
            products.Add(NewProduct(host, "acme", "OTHER"));
            (await failing.CommitAsync(Ct)).IsSuccess.ShouldBeFalse();
        }

        await using var next = await uow.BeginAsync(Ct);
        products.Add(NewProduct(host, "orbitly", "ORB"));
        (await next.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Beginning_a_second_scope_while_one_is_active_is_a_programming_error()
    {
        await using var host = Host();
        await using var scope = host.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var first = await uow.BeginAsync(Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => uow.BeginAsync(Ct));
    }

    [Fact]
    public async Task Committing_a_scope_twice_is_a_programming_error()
    {
        await using var host = Host();
        await using var scope = host.CreateScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        await work.CommitAsync(Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => work.CommitAsync(Ct));
    }

    private static async Task SeedAsync(PersistenceTestHost host, Product product)
    {
        await using var scope = host.CreateScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        scope.ServiceProvider.GetRequiredService<IProductRepository>().Add(product);
        (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
    }
}
