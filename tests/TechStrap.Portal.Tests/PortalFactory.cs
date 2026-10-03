using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Portal.Tests;

/// <summary>Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests.</summary>
internal sealed class PortalFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Portal.Program>
{
    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }
}
