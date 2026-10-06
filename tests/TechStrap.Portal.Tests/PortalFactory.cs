using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// The two required settings (the API address and the public URL) get test values; <paramref name="settings"/> is applied on top, so a test can blank one to prove the start fails.
/// </summary>
internal sealed class PortalFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Portal.Program>
{
    public const string ApiBaseUrl = "http://api.test/";
    public const string PublicUrl = "https://portal.test";

    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PortalOptions.ApiBaseUrlKey] = ApiBaseUrl,
                [PortalOptions.PublicUrlKey] = PublicUrl,
            });
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}
