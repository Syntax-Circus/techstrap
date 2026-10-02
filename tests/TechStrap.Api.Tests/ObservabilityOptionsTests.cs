using Microsoft.Extensions.Configuration;
using SyntaxCircus.Observability;

namespace TechStrap.Api.Tests;

/// <summary>
/// The Observability package binds the OpenTelemetry and Sentry sections eagerly in Program.cs, so a
/// factory cannot override them. These tests bind the same sections the host binds, from the Api's
/// checked-in appsettings.json plus explicit values, and prove they bind without error.
/// </summary>
public sealed class ObservabilityOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] overrides)
    {
        var apiDirectory = Path.Combine(ProjectRoot(), "src", "TechStrap.Api");
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(overrides.ToDictionary(o => o.Key, o => o.Value))
            .Build();
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_checked_in_defaults_bind_and_leave_export_and_sentry_disabled()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration());

        options.OpenTelemetry.Enabled.ShouldBeFalse();
        options.OpenTelemetry.IsEnabled.ShouldBeFalse();
        options.OpenTelemetry.OtlpProtocol.ShouldBe("grpc");
        options.Sentry.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void An_enabled_exporter_with_a_valid_endpoint_binds_and_turns_on()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration(
            ("OpenTelemetry:Enabled", "true"),
            ("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"),
            ("Sentry:Dsn", "https://key@example.invalid/1")));

        options.OpenTelemetry.IsEnabled.ShouldBeTrue();
        options.OpenTelemetry.StartupWarning.ShouldBeNull();
        options.Sentry.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void An_enabled_exporter_without_an_endpoint_reports_a_startup_warning_instead_of_failing()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration(
            ("OpenTelemetry:Enabled", "true"),
            ("OpenTelemetry:OtlpEndpoint", "")));

        options.OpenTelemetry.IsEnabled.ShouldBeFalse();
        options.OpenTelemetry.StartupWarning.ShouldNotBeNull();
    }
}
