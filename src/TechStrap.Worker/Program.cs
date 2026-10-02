using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Infrastructure.Persistence;

const string ServiceName = "techstrap-worker";

// The Worker is a web host only so it can expose /health/*. It never migrates the database: the
// API owns migrations. Background loops arrive in PHASE-05.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddTechStrapPersistence();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseCorrelationId();
app.MapStandardHealthChecks();

app.Run();

namespace TechStrap.Worker
{
    public partial class Program;
}
