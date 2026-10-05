namespace TechStrap.Api.Tests.Config;

/// <summary>
/// A Production host given only the blank deploy template (<c>deploy/.env.&lt;app&gt;.example</c> as an operator copies it, before filling it in) must refuse to start and
/// name what is missing. The compose file supplies a few values of its own (the trusted networks, the Api address, the storage path); a second test adds them, so the
/// failure that remains is exactly the operator's to fix. Nothing here may start a host that is half configured.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionBlankTemplateTests
{
    // What deploy/docker-compose.yml sets in environment: for TrustedProxy (read eagerly, so it must be a real environment variable, as in a container).
    private const string ComposeSubnet = "172.16.31.0/24";

    // The test issuer HostFactory leaves in the environment would hide the missing Authority and audience.
    private static readonly (string, string?)[] CleanEnvironment =
    [
        ("DotEnv__Enabled", "false"),
        ("TrustedProxy__TrustedNetworks__0", null),
        ("TrustedProxy__TrustedNetworks__1", null),
        ("Authentication__JwtBearer__Authority", null),
        ("Authentication__JwtBearer__Audiences__0", null),
    ];

    public static TheoryData<HostKind> WebHosts() => new() { HostKind.Api, HostKind.Admin, HostKind.Portal };

    // What each host reports when the template is the only configuration. The Portal reads nothing required yet, so only the trusted proxies (which compose supplies) stop it.
    public static TheoryData<HostKind, string[]> HostsAndTheKeysTheyReportAlone() => new()
    {
        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "Storage:Local:RootPath"] },
        { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
        { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret", "Api:BaseUrl"] },
        { HostKind.Portal, ["TrustedProxy"] },
    };

    // What remains once compose has set its own values: exactly what the operator must fill in.
    public static TheoryData<HostKind, string[]> HostsAndTheKeysTheOperatorMustFill() => new()
    {
        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL"] },
        { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
        { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret"] },
    };

    /// <summary>The blank template as the host sees it, plus the Database setting every test host needs so the Api does not migrate a database that is not there.</summary>
    private static Dictionary<string, string?> BlankTemplate(HostKind host)
    {
        var settings = new Dictionary<string, string?>(ConfigFiles.ReadEnvFile(ConfigFiles.DeployTemplate(host)), StringComparer.OrdinalIgnoreCase)
        {
            ["Database:MigrateOnStartup"] = "false",
        };

        return settings;
    }

    [Theory]
    [MemberData(nameof(HostsAndTheKeysTheyReportAlone))]
    public async Task The_blank_template_alone_fails_start_naming_what_is_missing(HostKind host, string[] expectedKeys)
    {
        using var environment = new ScopedEnvironment(CleanEnvironment);

        var failure = await ConfigHosts.TryStartAsync(host, "Production", BlankTemplate(host));

        failure.ShouldNotBeNull($"{host} started in Production from the blank template alone");
        AssertNames(failure, host, expectedKeys);
    }

    [Theory]
    [MemberData(nameof(HostsAndTheKeysTheOperatorMustFill))]
    public async Task With_the_compose_values_the_blank_template_still_fails_start_naming_every_key_the_operator_must_fill(HostKind host, string[] requiredKeys)
    {
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var composeTrust = new ScopedEnvironment(("TrustedProxy__TrustedNetworks__0", ComposeSubnet));
        var settings = BlankTemplate(host);
        settings["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-blank-template-storage");
        settings["Api:BaseUrl"] = "http://api/";

        var failure = await ConfigHosts.TryStartAsync(host, "Production", settings);

        failure.ShouldNotBeNull($"{host} started in Production from the blank template");
        AssertNames(failure, host, requiredKeys);
    }

    private static void AssertNames(Exception failure, HostKind host, string[] keys)
    {
        // The messages only (the exception and everything inside it), never the stack frames: a frame must not satisfy a key name. Case-sensitive on purpose.
        var text = string.Join(Environment.NewLine, Messages(failure));
        foreach (var key in keys)
        {
            text.ShouldContain(key, Case.Sensitive, $"The {host} start-up failure must name {key}:{Environment.NewLine}{text}");
        }
    }

    private static IEnumerable<string> Messages(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Messages))
                {
                    yield return inner;
                }
            }
        }
    }

    // Valid fake values for every key the operator must fill, as they would be in /etc/techstrap/<env>/.env.<app>. The Api reads the issuer eagerly, so it is an environment variable.
    private static readonly Dictionary<HostKind, string[]> FilledValues = new()
    {
        [HostKind.Api] =
        [
            "ConnectionStrings:TechStrap=Host=127.0.0.1;Port=1;Database=techstrap;Username=techstrap;Password=replace-me",
            "Authentication:JwtBearer:Authority=https://idp.example.com/application/o/techstrap/",
            "Authentication:JwtBearer:Audiences:0=techstrap-api",
            "TECHSTRAP_PORTAL_PUBLIC_URL=https://support.example.com",
        ],
        [HostKind.Worker] =
        [
            "ConnectionStrings:TechStrap=Host=127.0.0.1;Port=1;Database=techstrap;Username=techstrap;Password=replace-me",
            "Email:Smtp:Host=smtp.example.com",
            "Email:Smtp:DefaultFrom=support@example.com",
        ],
        [HostKind.Admin] =
        [
            "Auth:Authority=https://idp.example.com/application/o/techstrap-admin/",
            "Auth:ClientId=techstrap-admin",
            "Auth:ClientSecret=not-a-real-secret",
        ],
    };

    public static TheoryData<HostKind> FilledHosts() => new() { HostKind.Api, HostKind.Worker, HostKind.Admin };

    [Theory]
    [MemberData(nameof(FilledHosts))]
    public async Task The_template_with_every_required_key_filled_and_the_compose_values_applied_starts_in_Production(HostKind host)
    {
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var composeTrust = new ScopedEnvironment(
            ("TrustedProxy__TrustedNetworks__0", ComposeSubnet),
            ("Authentication__JwtBearer__Authority", host == HostKind.Api ? "https://idp.example.com/application/o/techstrap/" : null),
            ("Authentication__JwtBearer__Audiences__0", host == HostKind.Api ? "techstrap-api" : null));
        var settings = BlankTemplate(host);
        settings["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-filled-template-storage");
        settings["Api:BaseUrl"] = "http://api/";
        foreach (var setting in FilledValues[host])
        {
            var parts = setting.Split('=', 2);
            settings[parts[0]] = parts[1];
        }

        var failure = await ConfigHosts.TryStartAsync(host, "Production", settings);

        failure.ShouldBeNull($"{host} did not start from the filled template: {failure}");
    }

    [Fact]
    public async Task The_Portal_has_no_required_setting_yet_so_the_blank_template_with_the_compose_trust_starts()
    {
        // PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL to the Portal and to this list; until then the Portal reads only optional settings.
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var composeTrust = new ScopedEnvironment(("TrustedProxy__TrustedNetworks__0", ComposeSubnet));

        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));

        failure.ShouldBeNull($"The Portal did not start from the blank template with the compose trust: {failure}");
    }

    [Theory]
    [MemberData(nameof(WebHosts))]
    public void No_deploy_template_leaves_an_array_element_blank(HostKind host)
    {
        // A blank element still counts as configured: TrustedProxy then sees one entry and Production stops failing fast.
        // The one exception is the Api audience, which the Api validates itself (a blank or whitespace audience is rejected), so it is a required key like any other.
        var blankElements = File.ReadAllLines(ConfigFiles.DeployTemplate(host))
            .Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z][A-Za-z0-9_]*__\d+=\s*$"))
            .Where(line => !line.StartsWith("AUTHENTICATION__JWTBEARER__AUDIENCES__0=", StringComparison.Ordinal))
            .ToList();

        blankElements.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_blank_array_element_defeats_the_trusted_proxy_check_which_is_why_the_templates_comment_it_out()
    {
        // Documents the trap: this host starts in Production with a blank trusted network, so the template must never contain one.
        // The value is " " (one space) because on Windows SetEnvironmentVariable(name, "") deletes the variable; a "KEY=" line in a container's env_file is the real case
        // (an empty string that still counts as one configured element), and a whitespace string binds the same way.
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var blankElement = new ScopedEnvironment(("TrustedProxy__TrustedProxies__0", " "));

        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));

        failure.ShouldBeNull($"A blank element was expected to satisfy the check: {failure}");
    }
}
