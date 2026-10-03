using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Admin.Components;

const string ServiceName = "techstrap-admin";

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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseForwardedHeaders();
app.UseCorrelationId();
// An address that matches no page gets the branded 404 (re-executed, so the 404 status code is kept).
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
app.MapRazorComponentsWithStaticAssets<App>()
    .AddInteractiveServerRenderMode();

app.Run();

namespace TechStrap.Admin
{
    public partial class Program;
}
