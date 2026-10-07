using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Api;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

//// <summary>
/// Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// The two required settings (the API address and the public URL) get test values; <paramref name="settings"/> is applied on top, so a test can blank one to prove the start fails.
/// A stub API (<see cref="Api"/>) sits behind the Portal's two named HTTP clients, and <see cref="LogSink"/> records every log event.
/// </summary>
internal sealed class PortalFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Portal.Program>
{
    public const string ApiBaseUrl = "http://api.test/";
    public const string PublicUrl = "https://portal.test";

    /// <summary>Every level is captured, including the Trace and Debug lines of System.Net.Http and ASP.NET Core: a secret that only shows at Verbose is still a leak.</summary>
    public static IReadOnlyDictionary<string, string?> VerboseLogging { get; } = new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    };

    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    /// <summary>The stub behind the Portal's API clients. Unconfigured calls answer 404 "stub-not-configured".</summary>
    public StubApiHandler Api { get; } = new();

    public CollectingSink LogSink { get; } = new();

    /// <summary>
    /// When set, disposing the factory asserts that every API call the host made carried <c>X-Forwarded-For: {ExpectedClientIp}</c> (<c>AssertEveryCallBore</c>), so a host test that makes an API call can never
    /// pass with a lost forwarded-IP handler (Review Focus 5). A test that made no call passes. The form and ticket kits set it for every host they build.
    /// </summary>
    public string? ExpectedClientIp { get; set; }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (ExpectedClientIp is not null && Api.Requests.Count > 0)
            {
                Api.AssertEveryCallBore(ExpectedClientIp);
            }
        }
        finally
        {
            await base.DisposeAsync();
        }
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
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            services.AddStubApi(Api);
            configureServices?.Invoke(services);
        });
    }
}
