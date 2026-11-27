using TechStrap.AppHost.Authentik;

namespace TechStrap.AppHost;

/// <summary>The whole local stack. Separate from the entry point so tests can inspect the model without running it.</summary>
public static class LocalStackComposition
{
    public static LocalApplications AddLocalStack(this IDistributedApplicationBuilder builder, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var options = LocalStackOptions.FromConfiguration(builder.Configuration);

        // Generated on the first run and persisted under ~/.aspire/secrets/<UserSecretsId>/ (not the dotnet user-secrets store), so a fresh
        // clone needs no setup and the values stay stable. No special characters, so they are safe in connection strings and environment variables.
        IResourceBuilder<ParameterResource> GeneratedSecret(string name, int minLength) =>
            builder.AddParameter(
                name,
                new GenerateParameterDefault { MinLength = minLength, Special = false },
                secret: true,
                persist: true);

        // Fixed, obviously fake dev logins. The values are in appsettings.Development.json (Parameters section).
        IResourceBuilder<ParameterResource> DevLogin(string name) => builder.AddParameter(name, secret: true);

        var postgres = builder
            .AddPostgres(
                "postgres",
                userName: builder.AddParameter("postgres-username", "techstrap"),
                password: GeneratedSecret("postgres-password", 32),
                port: LocalStackPorts.Postgres)
            // Must come before WithDataVolume: the data path is chosen from the image tag.
            .WithImageTag("17")
            .WithDataVolume("techstrap-local-postgres")
            .WithLifetime(ContainerLifetime.Persistent);

        var techstrapDb = postgres.AddDatabase("techstrap-db", "techstrap");
        var authentikDb = postgres.AddDatabase("authentik-db", "authentik");

        // Catches all local mail: nothing leaves the machine.
        var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.31.4")
            .WithEndpoint(port: LocalStackPorts.MailpitSmtp, targetPort: 1025, name: "smtp")
            .WithHttpEndpoint(port: LocalStackPorts.MailpitUi, targetPort: 8025, name: "http")
            .WithHttpHealthCheck("/readyz", endpointName: "http")
            .WithLifetime(ContainerLifetime.Persistent);

        var authentikSecrets = new AuthentikSecrets(
            GeneratedSecret("authentik-secret-key", 50),
            DevLogin("authentik-akadmin-password"),
            DevLogin("authentik-sam-password"),
            DevLogin("authentik-riley-password"),
            DevLogin("authentik-casey-password"),
            GeneratedSecret("authentik-client-secret", 48));
        var authentik = builder.AddAuthentik(authentikDb, authentikSecrets);

        return builder.AddApplications(
            new LocalStackDependencies(repositoryRoot, options, techstrapDb, mailpit, authentik, authentikSecrets));
    }
}
