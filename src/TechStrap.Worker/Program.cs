using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Hosting.Logging;
using TechStrap.Hosting.Sentry;
using TechStrap.Hosting.Wiring;
using TechStrap.Infrastructure.Attachments;
using TechStrap.Infrastructure.AutoClose;
using TechStrap.Infrastructure.Email;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Worker.AutoClose;
using TechStrap.Worker.Outbox;

const string ServiceName = "techstrap-worker";

// The Worker is a web host only so it can expose /health/*. It never migrates the database: the
// API owns migrations. Its three background loops drain the email outbox (PHASE-05),
// auto-close Solved tickets (PHASE-06b) and sweep finished outbox rows after the retention window (PHASE-06c).
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName]);
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
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        // The same scrubbers as every other host: the Worker serves only health checks, but an exception message or breadcrumb it sends must not carry a credential or a search either.
        options.AddSensitiveHeaderScrubbing();
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
// No HttpClient the factory creates (the OTLP exporters' included) logs its request headers: the default logging writes Authorization and x-api-key at Trace.
builder.Services.AddTechStrapHttpClientDefaults();
builder.Services.AddTechStrapPersistence();
// A committed change is sent as a Postgres NOTIFY for the Api to relay to its hub (D-018). After AddTechStrapPersistence, whose null broadcaster this replaces.
builder.Services.AddTechStrapNotifyBroadcaster();
builder.Services.AddTechStrapMetrics();
builder.Services.AddTechStrapEmail(builder.Configuration);
// Uploaded product logos in emails (D-052): optional; blank keeps the linked logo.
builder.Services.AddTechStrapProductLogoUrls(builder.Configuration);
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddTechStrapAutoClose(builder.Configuration);
builder.Services.AddHostedService<AutoCloseWorker>();
builder.Services.AddTechStrapOutboxRetention(builder.Configuration);
builder.Services.AddHostedService<OutboxRetentionWorker>();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseCorrelationId();
app.MapStandardHealthChecks();

app.Run();

namespace TechStrap.Worker
{
    public partial class Program;
}
