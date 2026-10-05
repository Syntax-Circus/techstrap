using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.Observability;
using TechStrap.Hosting.Logging;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Hosting.Wiring;

public static class ObservabilityExtensions
{
    /// <summary>
    /// The telemetry, logging and Sentry wiring of a browser host (Admin, Portal): OpenTelemetry, Serilog with <see cref="PiiRedactionEnricher"/> (emails, tokens and
    /// hashes never reach a log), and, when Sentry is enabled, <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>. Health checks and the Blazor
    /// circuit's <c>/_blazor</c> transactions are not sampled. The host calls <c>telemetry.LogStartupWarning(app.Logger)</c> after it builds.
    /// </summary>
    public static SyntaxCircusObservabilityRegistration AddTechStrapObservability(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var telemetry = builder.AddSyntaxCircusObservability(serviceName);
        builder.AddStandardSerilog(configureEnrichment: logger =>
        {
            telemetry.ConfigureSerilog(logger);
            logger.Enrich.With<PiiRedactionEnricher>();
        });
        if (telemetry.Options.Sentry.IsEnabled)
        {
            builder.WebHost.UseSentry(options =>
            {
                telemetry.ConfigureSentry(options, context =>
                    context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
                        || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
                options.AddSensitiveHeaderScrubbing();
                options.AutoSessionTracking = false;
            });
        }

        return telemetry;
    }
}
