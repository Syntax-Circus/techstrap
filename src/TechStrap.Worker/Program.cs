using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Infrastructure.AutoClose;
using TechStrap.Infrastructure.Email;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Worker.AutoClose;
using TechStrap.Worker.Outbox;

const string ServiceName = "techstrap-worker";

// The Worker is a web host only so it can expose /health/*. It never migrates the database: the
// API owns migrations. Its two background loops drain the email outbox (PHASE-05)
// and auto-close Solved tickets (PHASE-06b).
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
builder.Services.AddTechStrapEmail(builder.Configuration);
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddTechStrapAutoClose(builder.Configuration);
builder.Services.AddHostedService<AutoCloseWorker>();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseCorrelationId();
app.MapStandardHealthChecks();

app.Run();

namespace TechStrap.Worker
{
    public partial class Program;
}
