using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Options;
using TechStrap.Hosting.Logging;
using TechStrap.Hosting.Sentry;

const string ServiceName = "techstrap-admin";

// The Admin app: this host never touches the database and never migrates. It calls the API only through the typed
// clients over TechStrap.Contracts (Clients/), with the signed-in agent's token.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
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

// Required settings are validated when the host starts (not read here), so a missing Auth or Api key stops the start with a clear message.
builder.Services.AddAdminOptions(builder.Configuration);
builder.Services.AddBlazorTokenForwarding(builder.Configuration, AdminOptionsRegistration.AuthSection);

builder.Services.AddAdminAuthentication();
builder.Services.AddCascadingAuthenticationState();
// The named API clients, the typed clients over them and the scoped AgentSession (the layout's AgentGate asks it who is signed in).
builder.Services.AddTechStrapApiClients();
builder.Services.AddShell();
builder.Services.AddTicketFeatures();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseForwardedHeaders();
app.UseCorrelationId();
if (!app.Environment.IsDevelopment())
{
    // Plain error page for unhandled exceptions (BRAND.md section 3); the branded window is for the Admin 404 only.
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

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
// Order matters: the token cache middleware needs the authenticated user and must run before antiforgery (SyntaxCircus.Blazor.Auth).
app.UseAuthentication();
app.UseAuthorization();
app.UseBlazorTokenCache();
app.UseAntiforgery();

// Health checks and static assets stay anonymous; the fallback policy requires a signed-in agent for everything else.
var anonymous = app.MapGroup(string.Empty).AllowAnonymous();
anonymous.MapStandardHealthChecks();
anonymous.MapStaticAssets();
app.MapAdminAuthEndpoints();
app.MapAttachmentPassThrough();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

namespace TechStrap.Admin
{
    public partial class Program;
}
