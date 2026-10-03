using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Npgsql;
using TechStrap.Api.Startup;
using TechStrap.Application.Seeding;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Api.Tests;

/// <summary>
/// Calls ApiStartupTasks.RunAsync directly with a hand-built service provider, so each
/// environment/flag combination runs through the production code path without starting a host.
/// </summary>
public sealed class ApiStartupTasksTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static readonly TimeSpan FailFastTimeout = TimeSpan.FromSeconds(30);

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static ServiceProvider Services(IConfiguration configuration, IDevelopmentDataSeeder seeder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddTechStrapPersistence();
        services.AddScoped(_ => seeder);
        return services.BuildServiceProvider();
    }

    private static async Task<int> CountAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"__EFMigrationsHistory\"";
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Startup_migrates_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var configuration = Configuration(("ConnectionStrings:TechStrap", connectionString));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        await ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken);

        (await CountAppliedMigrationsAsync(connectionString)).ShouldBe(ExpectedMigrations.Ids().Count);
    }

    [Fact]
    public async Task Migration_can_be_switched_off()
    {
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", UnreachableDatabase),
            (ApiStartupTasks.MigrateOnStartupKey, "false"));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        await Should.NotThrowAsync(() =>
            ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Development", "true", 1)]
    [InlineData("Development", "false", 0)]
    [InlineData("Development", null, 0)]
    [InlineData("Production", "true", 0)]
    [InlineData("Production", "false", 0)]
    [InlineData("Production", null, 0)]
    public async Task Seeder_runs_only_in_Development_with_the_flag_set(string environmentName, string? flag, int expectedCalls)
    {
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", UnreachableDatabase),
            (ApiStartupTasks.MigrateOnStartupKey, "false"),
            (ApiStartupTasks.SeedDevelopmentDataKey, flag));
        await using var services = Services(configuration, seeder);

        await ApiStartupTasks.RunAsync(services, Environment(environmentName), configuration, TestContext.Current.CancellationToken);

        await seeder.Received(expectedCalls).SeedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Startup_with_migration_enabled_and_a_blank_connection_string_fails_fast()
    {
        var configuration = Configuration(("ConnectionStrings:TechStrap", ""));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        var attempt = ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken);

        var finished = await Task.WhenAny(attempt, Task.Delay(FailFastTimeout, TestContext.Current.CancellationToken));

        finished.ShouldBeSameAs(attempt, "startup hung instead of failing fast");
        attempt.IsFaulted.ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(() => attempt);
    }

    [Fact]
    public async Task A_failing_seeder_aborts_startup_instead_of_being_swallowed()
    {
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        seeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("seed failed")));
        var configuration = Configuration(
            (ApiStartupTasks.MigrateOnStartupKey, "false"),
            (ApiStartupTasks.SeedDevelopmentDataKey, "true"));
        await using var services = Services(configuration, seeder);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            ApiStartupTasks.RunAsync(services, Environment("Development"), configuration, TestContext.Current.CancellationToken));

        exception.Message.ShouldBe("seed failed");
    }

    [Fact]
    public async Task Seeder_runs_after_the_migration_has_been_applied()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", connectionString),
            (ApiStartupTasks.SeedDevelopmentDataKey, "true"));
        var migrationsSeenBySeeder = -1;
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        seeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
            migrationsSeenBySeeder = await CountAppliedMigrationsAsync(connectionString));
        await using var services = Services(configuration, seeder);

        await ApiStartupTasks.RunAsync(services, Environment("Development"), configuration, TestContext.Current.CancellationToken);

        migrationsSeenBySeeder.ShouldBe(ExpectedMigrations.Ids().Count);
    }
}
