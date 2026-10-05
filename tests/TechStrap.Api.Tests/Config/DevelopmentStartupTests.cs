namespace TechStrap.Api.Tests.Config;

/// <summary>
/// Every host starts in Development from what a developer has after <c>cp .env.example .env.local</c>: the committed appsettings files plus the host's
/// <c>.env.example</c>, nothing else. This pins that the settings the files list are real defaults that pass the hosts' start-up validation, and that the
/// Admin starts from the Development placeholders alone for its sign-in settings (the example leaves them commented out).
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DevelopmentStartupTests
{
    // The background loops talk to a database and an SMTP server that do not exist in a test; the rest of the host is what this test starts.
    private static readonly Dictionary<string, string?> TestOnlyOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Database:MigrateOnStartup"] = "false",
        ["EmailOutbox:Enabled"] = "false",
        ["AutoClose:Enabled"] = "false",
        ["OutboxRetention:Enabled"] = "false",
    };

    public static TheoryData<HostKind> Hosts() => new() { HostKind.Api, HostKind.Worker, HostKind.Admin, HostKind.Portal };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_host_starts_in_Development_from_its_appsettings_and_its_env_example(HostKind host)
    {
        using var environment = new ScopedEnvironment(
            ("DotEnv__Enabled", "false"),
            ("TrustedProxy__TrustedNetworks__0", null),
            ("Authentication__JwtBearer__Authority", null),
            ("Authentication__JwtBearer__Audiences__0", null));
        var settings = new Dictionary<string, string?>(ConfigFiles.ReadEnvFile(ConfigFiles.HostExample(host)), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in TestOnlyOverrides)
        {
            settings[key] = value;
        }

        var failure = await ConfigHosts.TryStartAsync(host, "Development", settings);

        failure.ShouldBeNull($"{host} did not start in Development: {failure}");
    }

    // The few settings a host cannot default (an address, a path): everything else must come from the committed appsettings alone.
    public static TheoryData<HostKind, string[]> HostsAndTheirOnlyRequiredSettings() => new()
    {
        { HostKind.Api, ["TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082", "Storage:Local:RootPath=storage"] },
        { HostKind.Worker, [] },
        { HostKind.Admin, ["Api:BaseUrl=http://localhost:8080/"] },
        { HostKind.Portal, [] },
    };

    [Theory]
    [MemberData(nameof(HostsAndTheirOnlyRequiredSettings))]
    public async Task A_host_starts_in_Development_from_its_appsettings_and_only_the_settings_it_cannot_default(HostKind host, string[] required)
    {
        // This is what a blank number, flag or enum in appsettings.json breaks: the value is bound and validated at start, and a blank fails to convert.
        using var environment = new ScopedEnvironment(
            ("DotEnv__Enabled", "false"),
            ("TrustedProxy__TrustedNetworks__0", null),
            ("Authentication__JwtBearer__Authority", null),
            ("Authentication__JwtBearer__Audiences__0", null));
        var settings = new Dictionary<string, string?>(TestOnlyOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var setting in required)
        {
            var parts = setting.Split('=', 2);
            settings[parts[0]] = parts[1];
        }

        var failure = await ConfigHosts.TryStartAsync(host, "Development", settings);

        failure.ShouldBeNull($"{host} did not start from its appsettings: {failure}");
    }

    [Fact]
    public async Task The_Admin_starts_in_Development_without_any_sign_in_setting_of_its_own()
    {
        using var environment = new ScopedEnvironment(("DotEnv__Enabled", "false"), ("TrustedProxy__TrustedNetworks__0", null));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Api:BaseUrl"] = "http://localhost:8080/" };

        var failure = await ConfigHosts.TryStartAsync(HostKind.Admin, "Development", settings);

        failure.ShouldBeNull($"The Admin did not start from appsettings.Development.json alone: {failure}");
    }

    [Fact]
    public async Task The_Admin_does_not_start_in_Development_when_the_example_sign_in_settings_are_uncommented_and_blank()
    {
        // The reason the example keeps AUTH__* commented out: a blank value from .env.local replaces the Development placeholder.
        using var environment = new ScopedEnvironment(("DotEnv__Enabled", "false"), ("TrustedProxy__TrustedNetworks__0", null));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Api:BaseUrl"] = "http://localhost:8080/",
            ["Auth:Authority"] = string.Empty,
            ["Auth:ClientId"] = string.Empty,
            ["Auth:ClientSecret"] = string.Empty,
        };

        var failure = await ConfigHosts.TryStartAsync(HostKind.Admin, "Development", settings);

        failure.ShouldNotBeNull();
        failure.ToString().ShouldContain("Auth:Authority");
    }
}
