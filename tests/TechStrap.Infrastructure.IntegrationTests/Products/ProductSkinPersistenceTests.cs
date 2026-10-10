using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

/// <summary>D-053: the skin JSON round-trips through the repository, and a product written without one reads null.</summary>
public sealed class ProductSkinPersistenceTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string Skin = "{\"pack\":\"slate\",\"brand\":\"#112233\"}";

    [Fact]
    public async Task The_skin_json_round_trips_and_an_existing_row_reads_null()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var before = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Acme.Id, Ct)))!;
        before.SkinJson.ShouldBeNull();

        var committed = await host.CommitAsync(async sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            var product = (await products.GetByIdAsync(scenario.Acme.Id, Ct))!;
            product.SetSkinJson(Skin).IsSuccess.ShouldBeTrue();
            products.Update(product);
        });
        committed.IsSuccess.ShouldBeTrue();

        var reread = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Acme.Id, Ct)))!;
        reread.SkinJson.ShouldBe(Skin);
    }
}
