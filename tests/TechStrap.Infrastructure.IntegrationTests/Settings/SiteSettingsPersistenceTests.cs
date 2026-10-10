using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests.Settings;

/// <summary>D-053: the migration seeds the one settings row, an update round-trips, and a stale update is a concurrency conflict.</summary>
public sealed class SiteSettingsPersistenceTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_seeded_row_reads_classic_and_an_update_round_trips()
    {
        await using var host = new PersistenceTestHost(Database);

        var first = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        first.DefaultPackKey.ShouldBe("classic");

        var committed = await host.CommitAsync(async sp =>
        {
            var settings = sp.GetRequiredService<ISiteSettingsRepository>();
            var loaded = await settings.GetAsync(Ct);
            loaded.SetDefaultPack("midnight").IsSuccess.ShouldBeTrue();
            settings.Update(loaded);
        });
        committed.IsSuccess.ShouldBeTrue();

        var second = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        second.DefaultPackKey.ShouldBe("midnight");
    }

    [Fact]
    public async Task A_stale_settings_update_is_a_concurrency_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstRepo = first.ServiceProvider.GetRequiredService<ISiteSettingsRepository>();
        var secondRepo = second.ServiceProvider.GetRequiredService<ISiteSettingsRepository>();
        var byFirst = await firstRepo.GetAsync(Ct);
        var bySecond = await secondRepo.GetAsync(Ct);
        byFirst.SetDefaultPack("slate").IsSuccess.ShouldBeTrue();
        bySecond.SetDefaultPack("paper").IsSuccess.ShouldBeTrue();

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstRepo.Update(byFirst);
        var firstResult = await firstWork.CommitAsync(Ct);
        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondRepo.Update(bySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        (await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct))).DefaultPackKey.ShouldBe("slate");
    }
}
