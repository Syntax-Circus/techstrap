using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// A real service provider with <c>AddTechStrapPersistence()</c> pointed at one test database, so repository tests exercise the
/// production registrations. Each <see cref="CreateScope"/> is one request or worker iteration: its own DbContext and connection.
/// </summary>
internal sealed class PersistenceTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public PersistenceTestHost(TestDatabase database, FakeTimeProvider? clock = null, Action<IServiceCollection>? configure = null)
    {
        Clock = clock ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{TechStrapDatabase.ConnectionStringName}"] = PooledConnectionString(database) })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddTechStrapPersistence();
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public FakeTimeProvider Clock { get; }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    /// <summary>
    /// Runs <paramref name="work"/> in its own scope inside one unit of work and commits. Returns the commit result so tests can
    /// assert conflicts; the work may read and stage anything through the scope's service provider.
    /// </summary>
    public async Task<Result> CommitAsync(Func<IServiceProvider, Task> work)
    {
        await using var scope = CreateScope();
        await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(TestContext.Current.CancellationToken);
        await work(scope.ServiceProvider);
        return await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs a read in a fresh scope (a fresh DbContext), so it sees only what was committed.</summary>
    public async Task<T> ReadAsync<T>(Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = CreateScope();
        return await read(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // Release this host's pooled connections so the per-test database can be dropped and sockets are not left lingering.
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>The fixture disables pooling; repository tests open many short connections, so they pool to avoid exhausting sockets.</summary>
    private static string PooledConnectionString(TestDatabase database) =>
        new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = true, MaxPoolSize = 80 }.ConnectionString;
}
