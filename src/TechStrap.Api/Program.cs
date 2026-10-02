using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Api.Startup;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Seeding;

const string ServiceName = "techstrap-api";

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
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapDevelopmentSeeding();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

await ApiStartupTasks.RunAsync(app.Services, app.Environment, app.Configuration);

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseProblemDetailsExceptionHandling();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
        diagnosticContext.Set("CorrelationId", CorrelationContextAccessor.CurrentCorrelationId);
});

app.MapStandardHealthChecks();
app.MapOpenApi();
app.MapControllers();

app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
