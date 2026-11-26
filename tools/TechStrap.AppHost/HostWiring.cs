using System.Globalization;
using Aspire.Hosting.Dotnet;
using Microsoft.Extensions.Logging;
using TechStrap.AppHost.Authentik;
using TechStrap.Infrastructure.Seeding;

namespace TechStrap.AppHost;

/// <summary>What the applications need from the rest of the stack.</summary>
public sealed record LocalStackDependencies(
    string RepositoryRoot,
    LocalStackOptions Options,
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<ContainerResource> Mailpit,
    IResourceBuilder<ContainerResource> Authentik,
    AuthentikSecrets AuthentikSecrets);

public sealed record LocalApplications(
    IResourceBuilder<DotnetProjectResource> Api,
    IResourceBuilder<DotnetProjectResource> Worker,
    IResourceBuilder<DotnetProjectResource> Admin,
    IResourceBuilder<DotnetProjectResource> Portal,
    IResourceBuilder<DotnetProjectResource> WebhookCatcher,
    IResourceBuilder<DotnetProjectResource> SampleClient);

/// <summary>
/// Everything the local applications are told. The four hosts ignore .env files here, so this is their whole
/// configuration beyond the committed appsettings. Public addresses come from <see cref="LocalStackPorts"/>: an
/// application's own listening port is random under Aspire's proxy.
/// </summary>
public static class HostWiring
{
    // The containers publish on 127.0.0.1 only. .NET tries ::1 first for "localhost", which costs about two seconds on every
    // new connection on Windows, so the hosts reach containers (and the catcher) by address.
    private const string Loopback = "127.0.0.1";

    // "localhost" must stay in the issuer: Authentik derives it from the request host.
    private static readonly string _issuer =
        $"http://localhost:{LocalStackPorts.Authentik}/application/o/{AuthentikResources.Slug}/";

    private static readonly string _apiUrl = $"https://localhost:{LocalStackPorts.ApiHttps}";
    private static readonly string _adminUrl = AuthentikResources.AdminOrigin;
    private static readonly string _portalUrl = $"https://localhost:{LocalStackPorts.PortalHttps}";
    private static readonly string _webhookCaptureUrl = $"http://{Loopback}:{LocalStackPorts.WebhookCatcher}/capture";
    private static readonly string _smtpPort = LocalStackPorts.MailpitSmtp.ToString(CultureInfo.InvariantCulture);

    public static LocalApplications AddApplications(
        this IDistributedApplicationBuilder builder, LocalStackDependencies stack)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(stack);

        var catcher = builder.AddLocalProject(stack, "webhook-catcher", "tools/TechStrap.WebhookCatcher")
            .WithHttpHealthCheck("/health", endpointName: "http");
        var api = builder.AddApi(stack);
        var worker = builder.AddWorker(stack, api, catcher);
        var admin = builder.AddAdmin(stack, api);
        var portal = builder.AddPortal(stack, api);
        var sample = builder.AddSampleClient(stack, api);

        return new LocalApplications(api, worker, admin, portal, catcher, sample);
    }

    private static IResourceBuilder<DotnetProjectResource> AddLocalProject(
        this IDistributedApplicationBuilder builder, LocalStackDependencies stack, string name, string folder)
        // The "aspire" profiles carry the fixed ports and the Development environment.
        => builder.AddDotnetProject(
            name, ProjectFile(stack, folder), options => options.LaunchProfileName = "aspire");

    private static string ProjectFile(LocalStackDependencies stack, string folder)
        => Path.GetFullPath(Path.Combine(stack.RepositoryRoot, folder, Path.GetFileName(folder) + ".csproj"));

    private static IResourceBuilder<DotnetProjectResource> AddApi(
        this IDistributedApplicationBuilder builder, LocalStackDependencies stack)
        => builder.AddLocalProject(stack, "api", "src/TechStrap.Api")
            .WithLocalHostDefaults()
            .WithEnvironment("ConnectionStrings__TechStrap", DatabaseConnectionString(stack.Database.Resource))
            .WithEnvironment("Authentication__JwtBearer__Authority", _issuer)
            .WithEnvironment("Authentication__JwtBearer__Audiences__0", AuthentikResources.ClientId)
            .WithEnvironment("Authentication__JwtBearer__RequireHttpsMetadata", "false")
            .WithEnvironment("TECHSTRAP_PORTAL_PUBLIC_URL", _portalUrl)
            .WithEnvironment("TECHSTRAP_API_PUBLIC_URL", _apiUrl)
            .WithEnvironment("TECHSTRAP_ADMIN_PUBLIC_URL", _adminUrl)
            .WithEnvironment("Storage__Provider", "Local")
            .WithEnvironment("Storage__Local__RootPath", StatePath(stack, "api", "storage"))
            .WithEnvironment("TECHSTRAP_SEED_DEV_DATA", stack.Options.SeedDevData ? "true" : "false")
            // The Portal forwards the visitor's address; here it calls from this machine.
            .WithEnvironment("TrustedProxy__TrustedNetworks__0", "127.0.0.1/32")
            .WithEnvironment("TrustedProxy__TrustedNetworks__1", "::1/128")
            .WithHttpHealthCheck("/health/ready", endpointName: "https")
            .WaitFor(stack.Database)
            .WaitFor(stack.Authentik);

    private static IResourceBuilder<DotnetProjectResource> AddWorker(
        this IDistributedApplicationBuilder builder,
        LocalStackDependencies stack,
        IResourceBuilder<DotnetProjectResource> api,
        IResourceBuilder<DotnetProjectResource> catcher)
        => builder.AddLocalProject(stack, "worker", "src/TechStrap.Worker")
            .WithLocalHostDefaults()
            .WithEnvironment("ConnectionStrings__TechStrap", DatabaseConnectionString(stack.Database.Resource))
            .WithEnvironment("TECHSTRAP_ADMIN_PUBLIC_URL", _adminUrl)
            .WithEnvironment("TECHSTRAP_API_PUBLIC_URL", _apiUrl)
            // Mail always goes to Mailpit: there is no real-mail switch. The retry values are docker-compose.yml's.
            .WithEnvironment("Email__Smtp__Host", Loopback)
            .WithEnvironment("Email__Smtp__Port", _smtpPort)
            .WithEnvironment("Email__Smtp__TlsMode", "None")
            .WithEnvironment("Email__Smtp__Username", string.Empty)
            .WithEnvironment("Email__Smtp__Password", string.Empty)
            .WithEnvironment("Email__Smtp__DefaultFrom", "support@techstrap.localhost")
            .WithEnvironment("Email__Smtp__MaxRetryAttempts", "1")
            .WithEnvironment("Email__Smtp__RetryMode", "TransientOnly")
            .WithEnvironment("Email__Smtp__TotalSendTimeout", "00:00:30")
            // Debug builds only (the Worker refuses to start otherwise): webhook deliveries go to the catcher.
            .WithEnvironment("TECHSTRAP_WEBHOOK_CAPTURE_URL", _webhookCaptureUrl)
            .WithHttpHealthCheck("/health/ready", endpointName: "http")
            // The Api migrates the database, so it must be healthy first.
            .WaitFor(api)
            .WaitFor(stack.Mailpit)
            .WaitFor(catcher);

    private static IResourceBuilder<DotnetProjectResource> AddAdmin(
        this IDistributedApplicationBuilder builder,
        LocalStackDependencies stack,
        IResourceBuilder<DotnetProjectResource> api)
        => builder.AddLocalProject(stack, "admin", "src/TechStrap.Admin")
            .WithLocalHostDefaults()
            .WithEnvironment("Auth__Authority", _issuer)
            .WithEnvironment("Auth__ClientId", AuthentikResources.ClientId)
            .WithEnvironment("Auth__ClientSecret", stack.AuthentikSecrets.ClientSecret)
            // User secrets must not move the local token cache to a hosted Redis (SyntaxCircus.Blazor.Auth reads this).
            .WithEnvironment("Auth__TokenCache__Redis__Enabled", "false")
            .WithBrowserHost(stack, "admin")
            .WithHttpHealthCheck("/health/live", endpointName: "https")
            .WaitFor(api);

    private static IResourceBuilder<DotnetProjectResource> AddPortal(
        this IDistributedApplicationBuilder builder,
        LocalStackDependencies stack,
        IResourceBuilder<DotnetProjectResource> api)
        => builder.AddLocalProject(stack, "portal", "src/TechStrap.Portal")
            .WithLocalHostDefaults()
            .WithBrowserHost(stack, "portal")
            .WithHttpHealthCheck("/health/live", endpointName: "https")
            .WaitFor(api);

    private static IResourceBuilder<DotnetProjectResource> AddSampleClient(
        this IDistributedApplicationBuilder builder,
        LocalStackDependencies stack,
        IResourceBuilder<DotnetProjectResource> api)
    {
        // A console program with no launch profile. It files one ticket and exits, so it runs only when asked.
        var sample = builder.AddDotnetProject(
                "sample-client",
                ProjectFile(stack, "samples/TechStrap.Client.Samples.Console"),
                options => options.ExcludeLaunchProfile = true)
            .WithEnvironment("TechStrap__BaseAddress", _apiUrl + "/")
            .WaitFor(api)
            .WithExplicitStart();

        // The key matches a product only in a database the development seeder filled.
        return stack.Options.SeedDevData
            ? sample.WithEnvironment("TechStrap__ApiKey", DevelopmentApiKeys.OrbitlyTrusted)
            : sample.WithEnvironment(context => context.Logger?.LogWarning(
                "LocalStack:SeedDevData is false, so {Resource} gets no API key. Create one in the Admin and set TechStrap__ApiKey.",
                sample.Resource.Name));
    }

    private static IResourceBuilder<DotnetProjectResource> WithLocalHostDefaults(
        this IResourceBuilder<DotnetProjectResource> host)
        => host
            // A developer's .env files must not point a local host somewhere else.
            .WithEnvironment("DotEnv__Enabled", "false")
            .WithEnvironment("Sentry__Dsn", string.Empty)
            // SyntaxCircus.Observability reads only the OpenTelemetry:* section, never the OTEL_* variables, so copy the
            // dashboard's endpoint, protocol and API-key header (callbacks run in registration order).
            .WithEnvironment(context =>
            {
                var variables = context.EnvironmentVariables;
                if (!variables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var endpoint))
                {
                    context.Logger?.LogWarning(
                        "Aspire did not provide OTEL_EXPORTER_OTLP_ENDPOINT, so {Resource} will not export telemetry.",
                        host.Resource.Name);
                    return;
                }

                variables["OpenTelemetry__Enabled"] = "true";
                variables["OpenTelemetry__OtlpEndpoint"] = endpoint;
                if (variables.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out var protocol))
                {
                    variables["OpenTelemetry__OtlpProtocol"] = protocol;
                }

                if (variables.TryGetValue("OTEL_EXPORTER_OTLP_HEADERS", out var headers))
                {
                    variables["OpenTelemetry__Headers"] = headers;
                }
            })
            .WithEnvironment("OpenTelemetry__TracesSampleRate", "1")
            .WithEnvironment("OpenTelemetry__Environment", "local");

    // What the Admin and the Portal share.
    private static IResourceBuilder<DotnetProjectResource> WithBrowserHost(
        this IResourceBuilder<DotnetProjectResource> host, LocalStackDependencies stack, string name)
        => host
            .WithEnvironment("Api__BaseUrl", _apiUrl + "/")
            .WithEnvironment("TECHSTRAP_PORTAL_PUBLIC_URL", _portalUrl)
            .WithEnvironment("DataProtection__KeyRingPath", StatePath(stack, "dataprotection", name));

    // State that belongs to the local database lives together, so a reset clears it all.
    private static string StatePath(LocalStackDependencies stack, string first, string second)
        => Path.Combine(stack.RepositoryRoot, "local", ".state", first, second);

    // Built from the resources' own parts, with the published port and the IPv4 loopback. The generated password has no
    // special characters, so it needs no quoting.
    private static ReferenceExpression DatabaseConnectionString(PostgresDatabaseResource database)
    {
        var port = LocalStackPorts.Postgres.ToString(CultureInfo.InvariantCulture);
        return ReferenceExpression.Create(
            $"Host={Loopback};Port={port};Username={database.Parent.UserNameReference};Password={database.Parent.PasswordParameter};Database={database.DatabaseName}");
    }
}
