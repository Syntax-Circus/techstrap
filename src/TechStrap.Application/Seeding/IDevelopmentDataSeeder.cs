namespace TechStrap.Application.Seeding;

/// <summary>
/// Dev-only startup hook (02-ARCHITECTURE.md section 7.6). The API calls it after migration, only in
/// Development with TECHSTRAP_SEED_DEV_DATA=true. It is a host startup step, not a use-case entry
/// point, so it is exempt from the handler rule and deliberately not named "...Handler".
/// </summary>
public interface IDevelopmentDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
