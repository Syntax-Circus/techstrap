namespace TechStrap.AppHost.Authentik;

/// <summary>Passwords are fixed dev values; the secret key and the client secret are generated and persisted.</summary>
public sealed record AuthentikSecrets(
    IResourceBuilder<ParameterResource> SecretKey,
    IResourceBuilder<ParameterResource> BootstrapPassword,
    IResourceBuilder<ParameterResource> SamPassword,
    IResourceBuilder<ParameterResource> RileyPassword,
    IResourceBuilder<ParameterResource> CaseyPassword,
    IResourceBuilder<ParameterResource> ClientSecret);

public static class AuthentikResources
{
    public const string Image = "goauthentik/server";
    public const string ImageTag = "2026.8.5";

    /// <summary>The application slug in the blueprint, which is part of the issuer URL.</summary>
    public const string Slug = "techstrap";

    public const string ClientId = "techstrap";

    /// <summary>The Admin's local origin: the only place the provider redirects to.</summary>
    public static readonly string AdminOrigin = $"https://localhost:{LocalStackPorts.AdminHttps}";

    /// <summary>
    /// Adds the Authentik server and worker (2026.8 needs no Redis) and returns the server. <c>WaitFor</c> on it also
    /// waits for the blueprint's provider.
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddAuthentik(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> db,
        AuthentikSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(secrets);

        var postgres = db.Resource.Parent;

        IResourceBuilder<ContainerResource> AddAuthentikContainer(string name, string command) =>
            builder.AddContainer(name, Image, ImageTag)
                .WithImageRegistry("ghcr.io")
                .WithArgs(command)
                .WithEnvironment("AUTHENTIK_SECRET_KEY", secrets.SecretKey)
                // Container-network values: postgres:5432, not the published host port.
                .WithEnvironment("AUTHENTIK_POSTGRESQL__HOST", postgres.Host)
                .WithEnvironment("AUTHENTIK_POSTGRESQL__PORT", postgres.Port)
                .WithEnvironment("AUTHENTIK_POSTGRESQL__NAME", db.Resource.DatabaseName)
                .WithEnvironment("AUTHENTIK_POSTGRESQL__USER", postgres.UserNameReference)
                .WithEnvironment("AUTHENTIK_POSTGRESQL__PASSWORD", postgres.PasswordParameter)
                // The akadmin account. Read only on the first start of an empty database, and by the worker too.
                .WithEnvironment("AUTHENTIK_BOOTSTRAP_PASSWORD", secrets.BootstrapPassword)
                .WithEnvironment("AUTHENTIK_BOOTSTRAP_EMAIL", "akadmin@techstrap.localhost")
                // The stack is self-contained: no update check, no startup analytics and no Gravatar avatars.
                .WithEnvironment("AUTHENTIK_DISABLE_UPDATE_CHECK", "true")
                .WithEnvironment("AUTHENTIK_DISABLE_STARTUP_ANALYTICS", "true")
                .WithEnvironment("AUTHENTIK_AVATARS", "initials")
                .WithVolume("techstrap-local-authentik-media", "/media")
                .WaitFor(db)
                .WithLifetime(ContainerLifetime.Persistent);

        var server = AddAuthentikContainer("authentik-server", "server")
            .WithHttpEndpoint(port: LocalStackPorts.Authentik, targetPort: 9000, name: "http")
            .WithHttpHealthCheck("/-/health/ready/")
            // The server reports ready long before the worker has applied the blueprint.
            .WithHttpHealthCheck($"/application/o/{Slug}/.well-known/openid-configuration");

        // The worker waits only for the server to start. The server's health needs the blueprint, which the worker
        // applies, so WaitFor(server) here would deadlock a cold start.
        AddAuthentikContainer("authentik-worker", "worker")
            .WithBindMount(
                Path.Combine(builder.AppHostDirectory, "Authentik", "blueprints"), "/blueprints/custom", isReadOnly: true)
            .WithEnvironment("TS_ADMIN_ORIGIN", AdminOrigin)
            .WithEnvironment("TS_CLIENT_SECRET", secrets.ClientSecret)
            .WithEnvironment("TS_SAM_PASSWORD", secrets.SamPassword)
            .WithEnvironment("TS_RILEY_PASSWORD", secrets.RileyPassword)
            .WithEnvironment("TS_CASEY_PASSWORD", secrets.CaseyPassword)
            .WaitForStart(server);

        return server;
    }
}
