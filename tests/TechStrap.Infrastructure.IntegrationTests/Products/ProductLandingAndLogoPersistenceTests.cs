using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

/// <summary>D-052: the three new columns round-trip, and a row written without them (an upgrade) reads as listed with no tagline or uploaded logo.</summary>
public sealed class ProductLandingAndLogoPersistenceTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_tagline_the_uploaded_logo_and_the_listed_flag_round_trip_through_the_repository()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var committed = await host.CommitAsync(async sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            var product = (await products.GetByIdAsync(scenario.Acme.Id, Ct))!;
            var b = product.Branding;
            var branding = ProductBranding.CreateForUpdate(b, b.DisplayName, b.LogoPath, b.AccentColour, null, null, "One line.").Value;
            product.UpdateDetails(product.Name, branding).IsSuccess.ShouldBeTrue();
            product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");
            product.SetListedOnLanding(false);
            products.Update(product);
        });
        committed.IsSuccess.ShouldBeTrue();

        var reread = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Acme.Id, Ct)))!;

        reread.Branding.Tagline.ShouldBe("One line.");
        reread.Branding.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        reread.ListedOnLanding.ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_inserted_without_the_new_columns_reads_as_listed_with_nothing_else()
    {
        await using var context = CreateDbContext();
        await RecordSeed.ProductAsync(context, "acme", "ACME");
        await context.Database.ExecuteSqlRawAsync("UPDATE products SET listed_on_landing = DEFAULT, tagline = NULL, uploaded_logo = NULL WHERE key = 'acme'", Ct);

        var record = await context.Set<ProductRecord>().AsNoTracking().SingleAsync(p => p.Key == "acme", Ct);

        record.ListedOnLanding.ShouldBeTrue();
        record.Tagline.ShouldBeNull();
        record.UploadedLogo.ShouldBeNull();
    }
}
