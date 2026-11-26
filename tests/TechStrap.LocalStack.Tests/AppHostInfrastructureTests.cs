using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using TechStrap.AppHost;
using TechStrap.Tests.Shared;

namespace TechStrap.LocalStack.Tests;

/// <summary>
/// Inspects the containers the AppHost declares, without starting anything. Parameters and resource references stay as
/// expressions such as <c>{postgres-password.value}</c>, so no secret is ever generated or read.
/// </summary>
public sealed class AppHostInfrastructureTests
{
    private const string Server = "authentik-server";
    private const string Worker = "authentik-worker";

    private readonly Lazy<IDistributedApplicationBuilder> _stack = new(() =>
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            AssemblyName = typeof(LocalStackPorts).Assembly.GetName().Name,
        });
        builder.AddLocalStack(RepositoryRoot.Find());
        return builder;
    });

    private IResource Resource(string name) => _stack.Value.Resources.Single(resource => resource.Name == name);

    [Theory]
    [InlineData("postgres", "docker.io", "library/postgres", "17")]
    [InlineData("mailpit", null, "axllent/mailpit", "v1.31.4")]
    [InlineData(Server, "ghcr.io", "goauthentik/server", "2026.8.5")]
    [InlineData(Worker, "ghcr.io", "goauthentik/server", "2026.8.5")]
    public void Containers_use_the_pinned_images(string name, string? registry, string image, string tag)
    {
        var annotation = Resource(name).Annotations.OfType<ContainerImageAnnotation>().Last();

        annotation.Registry.ShouldBe(registry);
        annotation.Image.ShouldBe(image);
        annotation.Tag.ShouldBe(tag);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mailpit")]
    [InlineData(Server)]
    [InlineData(Worker)]
    public void Containers_are_persistent(string name)
    {
        Resource(name).Annotations.OfType<ContainerLifetimeAnnotation>().Last().Lifetime
            .ShouldBe(ContainerLifetime.Persistent);
    }

    [Theory]
    [InlineData("postgres", "techstrap-local-postgres", "/var/lib/postgresql/data")]
    [InlineData(Server, "techstrap-local-authentik-media", "/media")]
    [InlineData(Worker, "techstrap-local-authentik-media", "/media")]
    public void Data_lives_in_the_named_volumes(string name, string volume, string target)
    {
        var mount = Resource(name).Annotations.OfType<ContainerMountAnnotation>()
            .Where(annotation => annotation.Type == ContainerMountType.Volume)
            .ShouldHaveSingleItem();

        mount.Source.ShouldBe(volume);
        mount.Target.ShouldBe(target);
    }

    [Theory]
    [InlineData("postgres", "tcp", LocalStackPorts.Postgres, 5432)]
    [InlineData("mailpit", "smtp", LocalStackPorts.MailpitSmtp, 1025)]
    [InlineData("mailpit", "http", LocalStackPorts.MailpitUi, 8025)]
    [InlineData(Server, "http", LocalStackPorts.Authentik, 9000)]
    public void Host_ports_are_fixed(string name, string endpointName, int port, int targetPort)
    {
        var endpoint = Resource(name).Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == endpointName);

        endpoint.Port.ShouldBe(port);
        endpoint.TargetPort.ShouldBe(targetPort);
        // The host address the port is published on: loopback only, never every interface.
        endpoint.TargetHost.ShouldBe("localhost");
    }

    [Theory]
    [InlineData("techstrap-db", "techstrap")]
    [InlineData("authentik-db", "authentik")]
    public void Databases_are_declared_on_postgres(string name, string database)
    {
        var resource = Resource(name).ShouldBeOfType<PostgresDatabaseResource>();

        resource.DatabaseName.ShouldBe(database);
        resource.Parent.Name.ShouldBe("postgres");
        resource.Parent.UserNameReference.ValueExpression.ShouldBe("{postgres-username.value}");
        resource.Parent.PasswordParameter.Name.ShouldBe("postgres-password");
    }

    [Fact]
    public async Task Postgres_user_is_techstrap()
    {
        var user = (ParameterResource)Resource("postgres-username");

        (await user.GetValueAsync(TestContext.Current.CancellationToken)).ShouldBe("techstrap");
    }

    [Fact]
    public void Mailpit_is_healthy_when_ready()
    {
        HealthCheckKeys("mailpit").ShouldHaveSingleItem().ShouldContain("/readyz");
    }

    [Fact]
    public void Authentik_server_health_needs_readiness_and_the_blueprint_provider()
    {
        var keys = HealthCheckKeys(Server);

        keys.Count.ShouldBe(2);
        keys.ShouldContain(key => key.Contains("/-/health/ready/", StringComparison.Ordinal));
        // The server reports ready long before the worker has applied the blueprint.
        keys.ShouldContain(key => key.Contains(
            "/application/o/techstrap/.well-known/openid-configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Authentik_worker_waits_only_for_the_server_to_start()
    {
        // Waiting for a healthy server would deadlock a cold start: its health needs the blueprint the worker applies.
        var wait = Resource(Worker).Annotations.OfType<WaitAnnotation>()
            .Where(annotation => annotation.Resource.Name == Server)
            .ShouldHaveSingleItem();

        wait.WaitType.ShouldBe(WaitType.WaitUntilStarted);
    }

    [Theory]
    [InlineData(Server)]
    [InlineData(Worker)]
    public void Authentik_waits_for_its_database(string name)
    {
        Resource(name).Annotations.OfType<WaitAnnotation>()
            .ShouldContain(wait => wait.Resource.Name == "authentik-db" && wait.WaitType == WaitType.WaitUntilHealthy);
    }

    [Theory]
    [InlineData(Server, "server")]
    [InlineData(Worker, "worker")]
    public async Task Authentik_runs_as_server_or_worker(string name, string command)
    {
        var arguments = new List<object>();
        var context = new CommandLineArgsCallbackContext(arguments, Resource(name), TestContext.Current.CancellationToken);
        foreach (var annotation in Resource(name).Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        arguments.ShouldBe([command]);
    }

    [Fact]
    public void Blueprint_is_mounted_read_only_into_the_worker_alone()
    {
        var mount = Resource(Worker).Annotations.OfType<ContainerMountAnnotation>()
            .Where(annotation => annotation.Type == ContainerMountType.BindMount)
            .ShouldHaveSingleItem();

        mount.Target.ShouldBe("/blueprints/custom");
        mount.IsReadOnly.ShouldBeTrue();
        Path.GetFullPath(mount.Source!).ShouldBe(RepositoryRoot.Combine("tools", "TechStrap.AppHost", "Authentik", "blueprints"));
        File.Exists(Path.Combine(mount.Source!, "techstrap.yaml")).ShouldBeTrue();
        Resource(Server).Annotations.OfType<ContainerMountAnnotation>()
            .ShouldNotContain(annotation => annotation.Type == ContainerMountType.BindMount);
    }

    [Theory]
    [InlineData(Server)]
    [InlineData(Worker)]
    public async Task Authentik_gets_its_database_and_secrets(string name)
    {
        var environment = await EnvironmentAsync(name);

        environment.ShouldContainKeyAndValue("AUTHENTIK_SECRET_KEY", "{authentik-secret-key.value}");
        environment.ShouldContainKeyAndValue("AUTHENTIK_POSTGRESQL__NAME", "authentik");
        environment.ShouldContainKeyAndValue("AUTHENTIK_POSTGRESQL__USER", "{postgres-username.value}");
        environment.ShouldContainKeyAndValue("AUTHENTIK_POSTGRESQL__PASSWORD", "{postgres-password.value}");
        environment.ShouldContainKeyAndValue("AUTHENTIK_BOOTSTRAP_PASSWORD", "{authentik-akadmin-password.value}");
        environment.ShouldContainKey("AUTHENTIK_POSTGRESQL__HOST");
        environment.ShouldContainKey("AUTHENTIK_POSTGRESQL__PORT");
    }

    [Theory]
    [InlineData(Server)]
    [InlineData(Worker)]
    public async Task Authentik_makes_no_outbound_calls(string name)
    {
        var environment = await EnvironmentAsync(name);

        environment.ShouldContainKeyAndValue("AUTHENTIK_DISABLE_UPDATE_CHECK", "true");
        environment.ShouldContainKeyAndValue("AUTHENTIK_DISABLE_STARTUP_ANALYTICS", "true");
        // The default avatars come from Gravatar.
        environment.ShouldContainKeyAndValue("AUTHENTIK_AVATARS", "initials");
    }

    [Fact]
    public async Task Only_the_worker_gets_the_blueprint_inputs()
    {
        var worker = await EnvironmentAsync(Worker);
        var server = await EnvironmentAsync(Server);

        worker.Where(pair => pair.Key.StartsWith("TS_", StringComparison.Ordinal)).ToDictionary().ShouldBe(
            new Dictionary<string, string>
            {
                ["TS_ADMIN_ORIGIN"] = $"https://localhost:{LocalStackPorts.AdminHttps}",
                ["TS_CLIENT_SECRET"] = "{authentik-client-secret.value}",
                ["TS_SAM_PASSWORD"] = "{authentik-sam-password.value}",
                ["TS_RILEY_PASSWORD"] = "{authentik-riley-password.value}",
                ["TS_CASEY_PASSWORD"] = "{authentik-casey-password.value}",
            },
            ignoreOrder: true);
        server.Keys.ShouldNotContain(key => key.StartsWith("TS_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("postgres-password")]
    [InlineData("authentik-secret-key")]
    [InlineData("authentik-client-secret")]
    public void Machine_secrets_are_generated_and_persisted(string name)
    {
        var parameter = (ParameterResource)Resource(name);

        parameter.Secret.ShouldBeTrue();
        // persist: true wraps the generator so that the first value is saved to the AppHost's user secrets.
        parameter.Default.ShouldNotBeNull().GetType().Name.ShouldBe("UserSecretsParameterDefault");
    }

    [Theory]
    [InlineData("authentik-akadmin-password")]
    [InlineData("authentik-sam-password")]
    [InlineData("authentik-riley-password")]
    [InlineData("authentik-casey-password")]
    public void Dev_logins_are_secret_parameters(string name)
    {
        var parameter = (ParameterResource)Resource(name);

        parameter.Secret.ShouldBeTrue();
        parameter.Default.ShouldBeNull();
    }

    [Fact]
    public void Committed_parameters_are_exactly_the_four_dev_logins()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryRoot.Combine("tools", "TechStrap.AppHost", "appsettings.Development.json")));

        document.RootElement.GetProperty("Parameters").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString())
            .ShouldBe(
                new Dictionary<string, string?>
                {
                    ["authentik-akadmin-password"] = "TechStrap-Local-Dev-2026",
                    ["authentik-sam-password"] = "TechStrap-Local-Dev-2026",
                    ["authentik-riley-password"] = "TechStrap-Local-Dev-2026",
                    ["authentik-casey-password"] = "TechStrap-Local-Dev-2026",
                },
                ignoreOrder: true);
    }

    private List<string> HealthCheckKeys(string name) =>
        [.. Resource(name).Annotations.OfType<HealthCheckAnnotation>().Select(annotation => annotation.Key)];

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
            Logger = NullLogger.Instance,
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
}
