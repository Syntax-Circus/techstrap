using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Tests.Shared;
using TechStrap.Worker.Outbox;

namespace TechStrap.Api.Tests;

/// <summary>
/// Every host answers /health/live with 200. Api and Worker also gate /health/ready on Postgres;
/// Admin and Portal have no readiness checks yet (they report live only until PHASE-07 and PHASE-09
/// add an API reachability check), so their /health/ready is also 200.
/// The Worker is a WebApplication, so a WebApplicationFactory covers it; the compose healthcheck
/// (Task 13) covers the real container.
/// </summary>
public sealed class HostHealthSmokeTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static IReadOnlyDictionary<string, string?> ConnectionString(string value) =>
        new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = value };

    [Fact]
    public async Task Api_is_live()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Worker_is_live_and_ready_with_postgres()
    {
        await using var factory = new WorkerFactory(settings: ConnectionString(await postgres.CreateDatabaseAsync()));
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Worker_is_live_but_not_ready_when_the_database_is_unreachable()
    {
        await using var factory = new WorkerFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Worker_boots_with_the_outbox_enabled_and_smtp_configured()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, connectionString);
        await using (var context = new TechStrapDbContext(options.Options))
        {
            await context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = connectionString,
            ["EmailOutbox:Enabled"] = "true",
            ["Email:Smtp:Host"] = "localhost",
            ["Email:Smtp:DefaultFrom"] = "support@example.test",
        };
        await using var factory = new WorkerFactory(settings: settings);
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Services.GetServices<IHostedService>().OfType<EmailOutboxWorker>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Worker_refuses_to_boot_with_the_outbox_enabled_and_no_smtp_host()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = await postgres.CreateDatabaseAsync(),
            ["EmailOutbox:Enabled"] = "true",
        };
        await using var factory = new WorkerFactory(settings: settings);

        var error = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        error.Message.ShouldContain("Email:Smtp:Host");
    }

    [Fact]
    public async Task Admin_reports_live_only()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Portal_reports_live_only()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
