using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.DotEnv;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Options;
using TechStrap.Hosting.Security;
using TechStrap.Hosting.Wiring;

const string ServiceName = "techstrap-admin";

// The Admin app: this host never touches the database and never migrates. It calls the API only through the typed
// clients over TechStrap.Contracts (Clients/), with the signed-in agent's token.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddTechStrapObservability(ServiceName);

// Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
// shared with the Portal (TechStrap.Hosting).
// The Content-Security-Policy lets the sign-in and sign-out redirects reach the identity provider (form-action) and, in Development only, a product logo on localhost.
builder.Services.AddTechStrapWebHost(
    builder.Configuration,
    TechStrapCsp.ForBlazorApp([TechStrapCsp.OriginOf(builder.Configuration["Auth:Authority"])], allowLoopbackImages: builder.Environment.IsDevelopment()));

// Required settings are validated when the host starts (not read here), so a missing Auth or Api key stops the start with a clear message.
builder.Services.AddAdminOptions(builder.Configuration);
builder.Services.AddBlazorTokenForwarding(builder.Configuration, AdminOptionsRegistration.AuthSection);

builder.Services.AddAdminAuthentication();
builder.Services.AddCascadingAuthenticationState();
// The named API clients, the typed clients over them and the scoped AgentSession (the layout's AgentGate asks it who is signed in).
builder.Services.AddTechStrapApiClients();
builder.Services.AddShell();
builder.Services.AddTicketFeatures();
builder.Services.AddKbFeatures();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

// Forwarded headers, correlation id and security headers first, then the plain error page (BRAND.md section 3; the branded window is for the Admin 404 only)
// and the branded 404, which is the only status that is re-executed.
app.UseTechStrapWebHost(AttachmentPassThrough.Prefix);
app.UseTechStrapErrorPages();
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
