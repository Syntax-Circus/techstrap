using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Admin.Tests;

/// <summary>Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests.</summary>
internal sealed class AdminFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Admin.Program>
{
    static AdminFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }
}
