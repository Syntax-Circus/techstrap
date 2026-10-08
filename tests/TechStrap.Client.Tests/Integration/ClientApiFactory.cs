using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;

namespace TechStrap.Client.Tests.Integration;

/// <summary>
/// The Api host in-process (a slim copy of the Api.Tests HostFactory, which cannot be linked because it references the other hosts). Settings are lazily bound in-memory configuration;
/// the JWT issuer settings must arrive as environment variables because Program.cs reads them before the factory's configuration exists.
/// </summary>
internal sealed class ClientApiFactory(
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Api.Program>
{
    static ClientApiFactory()
    {
        // A developer's gitignored .env.local must never leak into tests.
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        foreach (var (key, value) in TestJwt.Settings)
        {
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiStartupTasks.MigrateOnStartupKey] = "false",
                ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://portal.test",
                ["TECHSTRAP_API_PUBLIC_URL"] = "https://api.test",
                ["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-tests-default-storage"),
            });
            configuration.AddInMemoryCollection(TestJwt.Settings);
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services =>
        {
            TestJwt.Configure(services);
            configureServices?.Invoke(services);
        });
    }
}
