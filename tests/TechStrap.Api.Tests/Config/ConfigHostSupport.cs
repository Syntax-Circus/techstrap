using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SyntaxCircus.DotEnv;

namespace TechStrap.Api.Tests.Config;

/// <summary>The four TechStrap hosts, named as their project folders and deploy templates are.</summary>
public enum HostKind
{
    Api,
    Worker,
    Admin,
    Portal,
}

/// <summary>Finds committed files and reads them the way a host does, so a test starts a host from exactly what an operator copies.</summary>
public static class ConfigFiles
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string HostExample(HostKind host) => Path.Combine(RepositoryRoot, "src", $"TechStrap.{host}", ".env.example");

    public static string DeployTemplate(HostKind host) => Path.Combine(RepositoryRoot, "deploy", $".env.{host.ToString().ToLowerInvariant()}.example");

    /// <summary>
    /// Every uncommented setting of an env file as configuration keys (<c>A__B</c> becomes <c>A:B</c>), read by the same loader the hosts use for <c>.env.local</c>.
    /// A blank value stays a blank value, exactly like a blank line in a container's <c>env_file</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ReadEnvFile(string path)
    {
        File.Exists(path).ShouldBeTrue($"{path} must exist");
        var directory = Directory.CreateTempSubdirectory("techstrap-envfile-");
        try
        {
            File.Copy(path, Path.Combine(directory.FullName, ".env.local"));
            var configuration = new ConfigurationBuilder().AddSyntaxCircusDotEnvFiles(directory.FullName).Build();
            return configuration.AsEnumerable().Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Starts a host with nothing but its committed appsettings and the settings given: none of the defaults <see cref="HostFactory{TProgram}"/> adds
/// (a portal URL, a storage path, the test issuer), so a missing required setting is missing here too. Eager reads (trusted proxies) cannot come from
/// <paramref name="settings"/>; set them as environment variables, as the compose files do.
/// </summary>
public sealed class ConfigHostFactory<TProgram>(string environment, IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}

/// <summary>Creates the right <see cref="ConfigHostFactory{TProgram}"/> for a <see cref="HostKind"/>, and starts it.</summary>
public static class ConfigHosts
{
    /// <summary>Starts the host and returns whatever the start threw (null when it started).</summary>
    public static async Task<Exception?> TryStartAsync(HostKind host, string environment, IReadOnlyDictionary<string, string?> settings)
    {
        switch (host)
        {
            case HostKind.Api:
                await using (var factory = new ConfigHostFactory<TechStrap.Api.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Worker:
                await using (var factory = new ConfigHostFactory<TechStrap.Worker.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Admin:
                await using (var factory = new ConfigHostFactory<TechStrap.Admin.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Portal:
                await using (var factory = new ConfigHostFactory<TechStrap.Portal.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(host), host, null);
        }
    }
}
