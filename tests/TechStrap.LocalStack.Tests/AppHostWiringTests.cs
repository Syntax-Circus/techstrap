using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using TechStrap.AppHost;
using TechStrap.Tests.Shared;

namespace TechStrap.LocalStack.Tests;

/// <summary>
/// Inspects the environment the AppHost gives each application, without starting anything. Parameters and resource
/// references stay as expressions such as <c>{postgres-password.value}</c>, so no secret is ever generated or read.
/// </summary>
public sealed class AppHostWiringTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=55210;Username={postgres-username.value};Password={postgres-password.value};Database=techstrap";

    private const string Issuer = "http://localhost:55290/application/o/techstrap/";
    private const string ApiUrl = "https://localhost:55230";
    private const string AdminUrl = "https://localhost:55240";
    private const string PortalUrl = "https://localhost:55250";
    private const string OtlpEndpoint = "http://localhost:55201";
    private const string OtlpApiKey = "test-otlp-api-key";

    private readonly Lazy<(IDistributedApplicationBuilder Builder, LocalApplications Apps)> _stack;
    private readonly Dictionary<string, string?> _appHostSettings = [];
    private readonly List<(LogLevel Level, string Message)> _log = [];

    public AppHostWiringTests() => _stack = new(() =>
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            AssemblyName = typeof(LocalStackPorts).Assembly.GetName().Name,
        });
        builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = OtlpEndpoint;
        // With a dashboard Aspire generates this key; these tests run without one.
        builder.Configuration["AppHost:OtlpApiKey"] = OtlpApiKey;
        foreach (var (key, value) in _appHostSettings)
        {
            builder.Configuration[key] = value;
        }

        return (builder, builder.AddLocalStack(RepositoryRoot.Find()));
    });

    private static string State(params string[] segments) =>
        Path.Combine([RepositoryRoot.Find(), "local", ".state", .. segments]);

    private IResource Resource(string name)
    {
        _ = _stack.Value;
        return _stack.Value.Builder.Resources.Single(resource => resource.Name == name);
    }

    [Fact]
    public void Applications_have_the_agreed_resource_names_and_projects()
    {
        var apps = _stack.Value.Apps;

        new[]
            {
                apps.Api.Resource, apps.Worker.Resource, apps.Admin.Resource, apps.Portal.Resource,
                apps.WebhookCatcher.Resource, apps.SampleClient.Resource,
            }
            .Select(resource => resource.Name)
            .ShouldBe(["api", "worker", "admin", "portal", "webhook-catcher", "sample-client"]);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    [InlineData("admin")]
    [InlineData("portal")]
    public async Task Every_host_ignores_dotenv_files(string host)
    {
        var environment = await EnvironmentAsync(host);

        environment.ShouldContainKeyAndValue("DotEnv__Enabled", "false");
        environment.ShouldContainKeyAndValue("Sentry__Dsn", string.Empty);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    [InlineData("admin")]
    [InlineData("portal")]
    public async Task Every_host_exports_telemetry_to_the_dashboard(string host)
    {
        var environment = await EnvironmentAsync(host);

        environment.ShouldContainKeyAndValue("OpenTelemetry__Enabled", "true");
        environment.ShouldContainKeyAndValue("OpenTelemetry__OtlpEndpoint", environment["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        environment.ShouldContainKeyAndValue("OpenTelemetry__OtlpProtocol", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        environment["OpenTelemetry__OtlpProtocol"].ShouldBe("grpc");
        // The dashboard accepts telemetry only with its API key, which Aspire passes as a header.
        environment.ShouldContainKeyAndValue("OTEL_EXPORTER_OTLP_HEADERS", $"x-otlp-api-key={OtlpApiKey}");
        environment.ShouldContainKeyAndValue("OpenTelemetry__Headers", environment["OTEL_EXPORTER_OTLP_HEADERS"]);

        environment.ShouldContainKeyAndValue("OpenTelemetry__TracesSampleRate", "1");
        environment.ShouldContainKeyAndValue("OpenTelemetry__Environment", "local");
    }

    [Fact]
    public async Task Api_gets_its_database_sign_in_urls_storage_and_proxy_trust()
    {
        var environment = await EnvironmentAsync("api");

        ShouldContain(environment, new()
        {
            ["ConnectionStrings__TechStrap"] = ConnectionString,
            ["Authentication__JwtBearer__Authority"] = Issuer,
            ["Authentication__JwtBearer__Audiences__0"] = "techstrap",
            ["Authentication__JwtBearer__RequireHttpsMetadata"] = "false",
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = PortalUrl,
            ["TECHSTRAP_API_PUBLIC_URL"] = ApiUrl,
            ["TECHSTRAP_ADMIN_PUBLIC_URL"] = AdminUrl,
            // Attachments stay on this disk, whatever else is configured for the developer.
            ["Storage__Provider"] = "Local",
            ["Storage__Local__RootPath"] = State("api", "storage"),
            ["TECHSTRAP_SEED_DEV_DATA"] = "true",
            ["TrustedProxy__TrustedNetworks__0"] = "127.0.0.1/32",
            ["TrustedProxy__TrustedNetworks__1"] = "::1/128",
        });
    }

    [Fact]
    public async Task Worker_gets_its_database_mail_public_urls_and_the_webhook_catcher()
    {
        var environment = await EnvironmentAsync("worker");

        ShouldContain(environment, new()
        {
            ["ConnectionStrings__TechStrap"] = ConnectionString,
            ["TECHSTRAP_ADMIN_PUBLIC_URL"] = AdminUrl,
            ["TECHSTRAP_API_PUBLIC_URL"] = ApiUrl,
            ["Email__Smtp__Host"] = "127.0.0.1",
            ["Email__Smtp__Port"] = "55220",
            ["Email__Smtp__TlsMode"] = "None",
            ["Email__Smtp__Username"] = string.Empty,
            ["Email__Smtp__Password"] = string.Empty,
            ["Email__Smtp__DefaultFrom"] = "support@techstrap.localhost",
            ["Email__Smtp__MaxRetryAttempts"] = "1",
            ["Email__Smtp__RetryMode"] = "TransientOnly",
            ["Email__Smtp__TotalSendTimeout"] = "00:00:30",
            ["TECHSTRAP_WEBHOOK_CAPTURE_URL"] = "http://127.0.0.1:55270/capture",
        });
    }

    [Fact]
    public async Task Admin_signs_in_against_local_Authentik_and_calls_the_local_Api()
    {
        var environment = await EnvironmentAsync("admin");

        ShouldContain(environment, new()
        {
            ["Auth__Authority"] = Issuer,
            ["Auth__ClientId"] = "techstrap",
            ["Auth__ClientSecret"] = "{authentik-client-secret.value}",
            // The token cache stays in process: the local stack has no Redis, and must not reach a hosted one.
            ["Auth__TokenCache__Redis__Enabled"] = "false",
            ["Api__BaseUrl"] = ApiUrl + "/",
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = PortalUrl,
            ["DataProtection__KeyRingPath"] = State("dataprotection", "admin"),
        });
    }

    [Fact]
    public async Task Portal_calls_the_local_Api()
    {
        var environment = await EnvironmentAsync("portal");

        ShouldContain(environment, new()
        {
            ["Api__BaseUrl"] = ApiUrl + "/",
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = PortalUrl,
            ["DataProtection__KeyRingPath"] = State("dataprotection", "portal"),
        });
    }

    [Theory]
    [InlineData(null, "true")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public async Task Seed_switch_reaches_the_Api(string? setting, string expected)
    {
        _appHostSettings["LocalStack:SeedDevData"] = setting;

        (await EnvironmentAsync("api"))["TECHSTRAP_SEED_DEV_DATA"].ShouldBe(expected);
    }

    [Fact]
    public async Task Sample_gets_the_local_Api_and_the_seeded_trusted_key_when_seeding()
    {
        var environment = await EnvironmentAsync("sample-client");

        ShouldContain(environment, new()
        {
            ["TechStrap__BaseAddress"] = ApiUrl + "/",
            ["TechStrap__ApiKey"] = "tsk_devOrbitlyServerKeyNotASecret00000000000000",
        });
        _log.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Sample_gets_no_key_and_warns_when_not_seeding()
    {
        _appHostSettings["LocalStack:SeedDevData"] = "false";

        var environment = await EnvironmentAsync("sample-client");

        environment.ShouldContainKeyAndValue("TechStrap__BaseAddress", ApiUrl + "/");
        environment.ShouldNotContainKey("TechStrap__ApiKey");
        _log.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("LocalStack:SeedDevData", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_sample_waits_for_a_manual_start()
    {
        Resource("sample-client").Annotations.OfType<ExplicitStartupAnnotation>().ShouldNotBeEmpty();
        foreach (var name in new[] { "api", "worker", "admin", "portal", "webhook-catcher" })
        {
            Resource(name).Annotations.OfType<ExplicitStartupAnnotation>().ShouldBeEmpty(name);
        }
    }

    [Theory]
    // Aspire also makes a database's waiter wait for its server.
    [InlineData("api", "postgres", "techstrap-db", "authentik-server")]
    [InlineData("worker", "api", "mailpit", "webhook-catcher")]
    [InlineData("admin", "api")]
    [InlineData("portal", "api")]
    [InlineData("sample-client", "api")]
    [InlineData("webhook-catcher")]
    public void Applications_wait_for_healthy_dependencies(string name, params string[] dependencies)
    {
        // Aspire adds its own build step ("__dotnet-project-build") to every project.
        var waits = Resource(name).Annotations.OfType<WaitAnnotation>()
            .Where(wait => !wait.Resource.Name.StartsWith("__", StringComparison.Ordinal))
            .ToList();

        waits.Select(wait => wait.Resource.Name).ShouldBe(dependencies, ignoreOrder: true);
        // The worker relies on this: the Api migrates the database before it reports healthy.
        waits.ShouldAllBe(wait => wait.WaitType == WaitType.WaitUntilHealthy);
    }

    [Theory]
    [InlineData("api", "/health/ready")]
    [InlineData("worker", "/health/ready")]
    [InlineData("admin", "/health/live")]
    [InlineData("portal", "/health/live")]
    [InlineData("webhook-catcher", "/health")]
    public void Applications_are_health_checked(string name, string path)
    {
        Resource(name).Annotations.OfType<HealthCheckAnnotation>().ShouldHaveSingleItem()
            .Key.ShouldContain($"_{path}_");
    }

    [Theory]
    [InlineData("api", LocalStackPorts.ApiHttps)]
    [InlineData("worker", LocalStackPorts.Worker)]
    [InlineData("admin", LocalStackPorts.AdminHttps)]
    [InlineData("portal", LocalStackPorts.PortalHttps)]
    [InlineData("webhook-catcher", LocalStackPorts.WebhookCatcher)]
    public void Applications_are_published_on_their_fixed_ports(string name, int port)
    {
        Resource(name).Annotations.OfType<EndpointAnnotation>().ShouldContain(endpoint => endpoint.Port == port);
    }

    private static void ShouldContain(Dictionary<string, string> environment, Dictionary<string, string> expected) =>
        expected.Keys.ToDictionary(key => key, key => environment.GetValueOrDefault(key, "(not set)"))
            .ShouldBe(expected, ignoreOrder: true);

    // Runs the resource's environment callbacks, as Aspire does at start, but leaves references unresolved.
    private async Task<Dictionary<string, string>> EnvironmentAsync(string name)
    {
        var variables = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            Resource(name),
            variables,
            TestContext.Current.CancellationToken)
        {
            Logger = new RecordingLogger(_log),
        };

        foreach (var annotation in Resource(name).Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
        {
            await annotation.Callback(context);
        }

        return variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                string text => text,
                IManifestExpressionProvider expression => expression.ValueExpression,
                var other => other.ToString() ?? string.Empty,
            });
    }

    private sealed class RecordingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((logLevel, formatter(state, exception)));
    }
}
