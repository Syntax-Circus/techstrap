using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

public sealed class ProductPortalHostPersistenceTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Task<bool> IsTakenAsync(PersistenceTestHost host, string name, Guid? except) =>
        host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().IsPortalHostTakenAsync(name, except, Ct));

    [Fact]
    public async Task Two_products_with_the_same_host_are_refused_by_the_unique_index()
    {
        await using var context = CreateDbContext();
        var first = await RecordSeed.ProductAsync(context, "acme", "ACME");
        first.PortalHost = "support.x.com";
        await context.SaveChangesAsync(Ct);

        var second = await RecordSeed.ProductAsync(context, "orbitly", "ORB");
        second.PortalHost = "support.x.com";

        var failure = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(Ct));
        (failure.InnerException as PostgresException)!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Many_products_without_a_host_are_allowed()
    {
        await using var context = CreateDbContext();
        await RecordSeed.ProductAsync(context, "acme", "ACME");
        await RecordSeed.ProductAsync(context, "orbitly", "ORB");

        (await context.Set<ProductRecord>().CountAsync(p => p.PortalHost == null, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task The_host_is_stored_and_restored_through_the_repository_and_is_found_as_taken()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var committed = await host.CommitAsync(async sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            var acme = (await products.GetByIdAsync(scenario.Acme.Id, Ct))!;
            acme.SetPortalHost("Support.Acme.com").IsSuccess.ShouldBeTrue();
            products.Update(acme);
        });
        committed.IsSuccess.ShouldBeTrue();

        var loaded = await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Acme.Id, Ct));
        loaded!.PortalHost.ShouldBe("support.acme.com");

        (await IsTakenAsync(host, "support.acme.com", null)).ShouldBeTrue();
        (await IsTakenAsync(host, "support.acme.com", scenario.Orbitly.Id)).ShouldBeTrue();
        (await IsTakenAsync(host, "support.acme.com", scenario.Acme.Id)).ShouldBeFalse();
        (await IsTakenAsync(host, "help.acme.com", null)).ShouldBeFalse();
    }
}
