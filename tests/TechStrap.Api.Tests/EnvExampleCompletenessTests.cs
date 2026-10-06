using System.Diagnostics;
using System.Text.RegularExpressions;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Observability;
using ObservabilitySentryOptions = SyntaxCircus.Observability.SentryOptions;
using TechStrap.Admin.Options;
using TechStrap.Api.Options;
using TechStrap.Api.Startup;
using TechStrap.Application.Email;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Application.Tickets.Customer;

namespace TechStrap.Api.Tests;

/// <summary>
/// Every .env.example must document each setting the host binds from an options class, plus the
/// TechStrap-specific variables the owner listed. A key counts as documented when it appears as
/// KEY=value or as a commented-out "# KEY=value" line.
/// </summary>
public sealed partial class EnvExampleCompletenessTests
{
    private static readonly Regex KeyLine = KeyLinePattern();

    [GeneratedRegex(@"^\s*#?\s*(?<key>[A-Za-z][A-Za-z0-9_]*)=", RegexOptions.Compiled)]
    private static partial Regex KeyLinePattern();

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Turns a configuration key (Section:Key) into the environment-variable form (SECTION__KEY).</summary>
    private static string ToEnvName(string configurationKey) => configurationKey.Replace(":", "__", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Environment names for every settable property of an options type bound under <paramref name="section"/>.</summary>
    private static IEnumerable<string> OptionKeys(Type optionsType, string section)
    {
        foreach (var property in optionsType.GetProperties().Where(p => p.SetMethod is { IsPublic: true }))
        {
            var key = $"{section}:{property.Name}";
            yield return typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string)
                ? ToEnvName(key + ":0")
                : ToEnvName(key);
        }
    }

    private static IEnumerable<string> ObservabilityKeys() =>
        OptionKeys(typeof(OpenTelemetryOptions), OpenTelemetryOptions.SectionName)
            .Concat(OptionKeys(typeof(ObservabilitySentryOptions), ObservabilitySentryOptions.SectionName));

    private static IEnumerable<string> TrustedProxyKeys() => OptionKeys(typeof(TrustedProxyOptions), TrustedProxyOptions.SectionName);

    public static TheoryData<string, string[]> Hosts() => new()
    {
        {
            "TechStrap.Api",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                .. OptionKeys(typeof(PublicRateLimitOptions), PublicRateLimitOptions.SectionName),
                .. OptionKeys(typeof(IntakeRateLimitOptions), IntakeRateLimitOptions.SectionName),
                .. OptionKeys(typeof(CustomerRateLimitOptions), CustomerRateLimitOptions.SectionName),
                .. OptionKeys(typeof(LostLinkOptions), LostLinkOptions.SectionName),
                ToEnvName(ApiStartupTasks.MigrateOnStartupKey),
                ApiStartupTasks.SeedDevelopmentDataKey,
                "CONNECTIONSTRINGS__TECHSTRAP",
                "TECHSTRAP_AGENT_GROUP",
                "TECHSTRAP_ADMIN_GROUP",
                "TECHSTRAP_GROUP_CLAIM_TYPE",
                "AUTHENTICATION__JWTBEARER__AUTHORITY",
                "AUTHENTICATION__JWTBEARER__AUDIENCES__0",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "TECHSTRAP_API_PUBLIC_URL",
                "TECHSTRAP_ADMIN_PUBLIC_URL",
                "STORAGE__LOCAL__ROOTPATH",
                AutoCloseOptions.DaysKey,
            ]
        },
        {
            "TechStrap.Worker",
            [
                .. ObservabilityKeys(),
                "CONNECTIONSTRINGS__TECHSTRAP",
                "EMAIL__SMTP__HOST",
                "EMAIL__SMTP__PORT",
                "EMAIL__SMTP__USERNAME",
                "EMAIL__SMTP__PASSWORD",
                "EMAIL__SMTP__DEFAULTFROM",
                "EMAIL__SMTP__MAXRETRYATTEMPTS",
                "EMAIL__SMTP__TLSMODE",
                "EMAIL__SMTP__RETRYMODE",
                "EMAIL__SMTP__TOTALSENDTIMEOUT",
                .. OptionKeys(typeof(EmailOutboxWorkerOptions), EmailOutboxWorkerOptions.SectionName),
                "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
                AutoCloseOptions.DaysKey,
                .. OptionKeys(typeof(AutoCloseOptions), AutoCloseOptions.SectionName).Where(k => k != "AUTOCLOSE__DAYS"),
                .. OptionKeys(typeof(OutboxRetentionOptions), OutboxRetentionOptions.SectionName),
            ]
        },
        {
            "TechStrap.Admin",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                "API__BASEURL",
                "API__TIMEOUTSECONDS",
                "AUTH__AUTHORITY",
                "AUTH__CLIENTID",
                "AUTH__CLIENTSECRET",
                AgentGroupOptions.AgentGroupKey,
                AgentGroupOptions.AdminGroupKey,
                AgentGroupOptions.GroupClaimTypeKey,
                "DATAPROTECTION__KEYRINGPATH",
            ]
        },
        {
            "TechStrap.Portal",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                "API__BASEURL",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "TECHSTRAP_PORTAL_DEFAULT_PRODUCT",
                "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
                "DATAPROTECTION__KEYRINGPATH",
            ]
        },
    };

    private static HashSet<string> DocumentedKeys(string host)
    {
        var path = Path.Combine(RepositoryRoot, "src", host, ".env.example");
        File.Exists(path).ShouldBeTrue($"{path} must exist");

        return File.ReadAllLines(path)
            .Select(line => KeyLine.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Env_example_documents_every_required_setting(string host, string[] requiredKeys)
    {
        var documented = DocumentedKeys(host);

        var missing = requiredKeys.Select(k => k.ToUpperInvariant()).Where(k => !documented.Contains(k)).Order().ToList();

        missing.ShouldBeEmpty($"{host}/.env.example is missing: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("TechStrap.Worker", "STORAGE__LOCAL__ROOTPATH")]
    [InlineData("TechStrap.Worker", "TECHSTRAP_PORTAL_PUBLIC_URL")]
    [InlineData("TechStrap.Portal", "STORAGE__LOCAL__ROOTPATH")]
    public void Env_example_does_not_document_a_key_the_host_never_reads(string host, string staleKey)
    {
        // The Worker registers no attachment storage and builds no portal links; the Portal never touches storage.
        DocumentedKeys(host).ShouldNotContain(staleKey);
    }

    [Fact]
    public void The_key_enumerator_produces_the_expected_names()
    {
        // Guards the helper itself: a wrong enumerator would make the theory above pass vacuously.
        TrustedProxyKeys().ShouldContain("TRUSTEDPROXY__TRUSTEDNETWORKS__0");
        TrustedProxyKeys().ShouldContain("TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION");
        OptionKeys(typeof(PublicRateLimitOptions), PublicRateLimitOptions.SectionName)
            .ShouldBe(["RATELIMITING__PUBLIC__PERMITLIMIT", "RATELIMITING__PUBLIC__WINDOWSECONDS"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("TechStrap.Api")]
    [InlineData("TechStrap.Worker")]
    [InlineData("TechStrap.Admin")]
    [InlineData("TechStrap.Portal")]
    public void Local_env_files_are_gitignored_and_examples_are_not(string host)
    {
        GitCheckIgnore($"src/{host}/.env.local").ShouldBe(0, ".env.local must be ignored");
        GitCheckIgnore($"src/{host}/.env.example").ShouldBe(1, ".env.example must be tracked");
    }

    private static int GitCheckIgnore(string relativePath)
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["check-ignore", "-q", relativePath])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
