using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// It supplies the settings the options validation requires (<see cref="AdminTestSettings"/>, with <c>settings</c> applied on top) and a stub API (<see cref="Api"/>).
/// </summary>
internal sealed class AdminFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Admin.Program>
{
    static AdminFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    /// <summary>The stub behind the Admin's API clients. The tasks that add the clients and the sign-in wire it in.</summary>
    public StubApiHandler Api { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(AdminTestSettings.With(settings)));
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}
