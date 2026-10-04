using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using TechStrap.Api.Startup;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests;

/// <summary>Captures every Serilog event the host writes so tests can assert on log lines.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}

/// <summary>
/// Starts one of the TechStrap hosts in-process. Settings are applied as lazily-bound in-memory
/// configuration. TrustedProxy is NOT overridable this way: AddTrustedProxyForwardedHeaders binds it
/// eagerly in Program.cs, before the factory's configuration is applied (see CLIENT_IP_RATE_LIMITING.md).
/// </summary>
public class HostFactory<TProgram>(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    static HostFactory()
    {
        // A developer's gitignored .env.local must never leak into tests.
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // AddSyntaxCircusJwtBearer reads Authority and Audiences while Program.cs builds the host, before the factory's
        // in-memory settings exist, so the test issuer must arrive as environment variables to reach the real
        // validation parameters (the same reason TrustedProxy cannot be overridden through settings).
        foreach (var (key, value) in Auth.TestJwt.Settings)
        {
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
        }
    }

    public CollectingSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Tests opt in to migration explicitly; most do not need a database at startup.
            // The intake settings are validated on start, so every host that registers intake needs a valid default.
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiStartupTasks.MigrateOnStartupKey] = "false",
                ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://portal.test",
                ["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-tests-default-storage"),
            });
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            configureServices?.Invoke(services);
        });
    }
}

/// <summary>The Api host with the locally signed test issuer (TestJwt) always configured, so any test can send a bearer token.</summary>
public sealed class ApiFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : HostFactory<TechStrap.Api.Program>(
        environment,
        Auth.TestJwt.Settings.Concat(settings ?? new Dictionary<string, string?>())
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
        services =>
        {
            Auth.TestJwt.Configure(services);
            configureServices?.Invoke(services);
        });

/// <summary>The Worker host. The email outbox and auto-close loops are off by default so smoke tests never start it against an unmigrated database; a test turns it on through <c>settings</c>.</summary>
public sealed class WorkerFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : HostFactory<TechStrap.Worker.Program>(
        environment,
        new Dictionary<string, string?> { ["EmailOutbox:Enabled"] = "false", ["AutoClose:Enabled"] = "false", ["OutboxRetention:Enabled"] = "false" }
            .Concat(settings ?? new Dictionary<string, string?>())
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
        configureServices);

/// <summary>
/// The Admin host with the settings its options validation requires (<see cref="AdminTestSettings"/>, with <c>settings</c> applied on top) and a stub API behind its
/// named HTTP clients (<see cref="Api"/>).
/// </summary>
public sealed class AdminFactory : HostFactory<TechStrap.Admin.Program>
{
    public AdminFactory(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
        : this(environment, settings, configureServices, new StubApiHandler().WithTestAgents())
    {
    }

    private AdminFactory(
        string environment,
        IReadOnlyDictionary<string, string?>? settings,
        Action<IServiceCollection>? configureServices,
        StubApiHandler api)
        : base(
            environment,
            AdminTestSettings.With(settings),
            services =>
            {
                services.AddAdminTestAuthentication();
                services.AddStubApi(api);
                configureServices?.Invoke(services);
            }) => Api = api;

    /// <summary>The stub behind the Admin's API clients. By default it answers GET /api/agents/me for the three test principals.</summary>
    public StubApiHandler Api { get; }
}

public sealed class PortalFactory(string environment = "Development")
    : HostFactory<TechStrap.Portal.Program>(environment);
