using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Portal.Tests;

/// <summary>Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.</summary>
internal sealed class PortalFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Portal.Program>
{
    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }
}
