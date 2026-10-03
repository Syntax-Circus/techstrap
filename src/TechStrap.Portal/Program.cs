using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Portal.Components;
using TechStrap.Portal.Components.Ui;

const string ServiceName = "techstrap-portal";

// Placeholder shell: this host never touches the database and never migrates. It will call the API
// through typed clients over TechStrap.Contracts once its UI phase lands.
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
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
                || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddHealthChecks();
// Trust X-Forwarded-* only from the reverse proxy. Production fails to start without configuration.
builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);
// Antiforgery and circuit state need a stable key ring; containers mount a volume here.
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

builder.Services.AddRazorComponents();
// Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
// A value that is not true or false fails at startup rather than breaking every page.
builder.Services.AddOptions<PoweredByOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        options.Show = !bool.TryParse(configuration[PoweredByOptions.ConfigurationKey], out var show) || show)
    .Validate<IConfiguration>(
        (options, configuration) => string.IsNullOrWhiteSpace(configuration[PoweredByOptions.ConfigurationKey])
            || bool.TryParse(configuration[PoweredByOptions.ConfigurationKey], out _),
        $"{PoweredByOptions.ConfigurationKey} must be true or false.")
    .ValidateOnStart();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseForwardedHeaders();
app.UseCorrelationId();
// An address that matches no page gets the not-found page (re-executed, so the 404 status code is kept).
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// BRAND.md section 3: humour never covers an error that blocks work, so only 404 is re-executed to the not-found page.
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode != StatusCodes.Status404NotFound)
    {
        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
    }
});
app.UseAntiforgery();
app.MapStandardHealthChecks();
app.MapRazorComponentsWithStaticAssets<App>();

app.Run();

namespace TechStrap.Portal
{
    public partial class Program;
}
