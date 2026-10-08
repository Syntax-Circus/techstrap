using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Intake;

namespace TechStrap.Client.Tests.Integration;

/// <summary>One migrated database, one running Api host over it and the intake seed (Orbitly with a Trusted and a Public key, a revoked key, a deactivated product), cleaned up on dispose.</summary>
internal sealed class ApiHarness : IAsyncDisposable
{
    private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.5");

    private readonly string _storage;

    private ApiHarness(ClientApiFactory factory, ApiTestDatabase database, IntakeSeed seed, string storage)
    {
        Factory = factory;
        Database = database;
        Seed = seed;
        _storage = storage;
    }

    public ClientApiFactory Factory { get; }

    public ApiTestDatabase Database { get; }

    internal IntakeSeed Seed { get; }

    /// <param name="kestrel">Host on a real loopback socket: TestServer has no request-body-size feature, so the 413 case needs the real server.</param>
    public static async Task<ApiHarness> StartAsync(TestPostgres postgres, bool kestrel = false, IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var storage = Path.Combine(Path.GetTempPath(), "techstrap-client-" + Guid.NewGuid().ToString("N"));
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = storage,
        };
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var factory = new ClientApiFactory(settings, services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(Peer)));
        if (kestrel)
        {
            factory.UseKestrel(0);
        }

        var seed = await IntakeTestData.SeedAsync(factory.Services, Xunit.TestContext.Current.CancellationToken);
        return new ApiHarness(factory, database, seed, storage);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }
}
