using Microsoft.Extensions.DependencyInjection.Extensions;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.DotEnv;
using TechStrap.Hosting.Security;
using TechStrap.Hosting.Wiring;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components;
using TechStrap.Portal.Components.Ui;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Headers;
using TechStrap.Portal.Products;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Suggestions;
using TechStrap.Portal.Tickets;
using TechStrap.Portal.Uploads;

const string ServiceName = "techstrap-portal";

// The public portal: static server-side rendering. This host never touches the database and never migrates; it calls the API (TechStrap.Contracts).
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddTechStrapObservability(ServiceName);

// Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
// shared with the Admin (TechStrap.Hosting).
// The Content-Security-Policy; the Portal has no sign-in, so form-action is this origin only. Loopback logos only in Development.
builder.Services.AddTechStrapWebHost(builder.Configuration, TechStrapCsp.ForBlazorApp(allowLoopbackImages: builder.Environment.IsDevelopment()));

builder.Services.AddRazorComponents();

// The API address, the Portal's public address and the optional default product: validated at start (D-043, D-045).
builder.Services.AddPortalOptions();

// The two named API clients (reads retried, writes never) and the typed clients: every call forwards the visitor's address (D-019, D-045).
builder.Services.AddPortalApiClients();

// The product the current request is about, read by the layout (one per request).
builder.Services.AddScoped<ProductScope>();

// The protected, 10-minute reference that carries a ticket number to the "received" page (D-045 addendum). It needs the data-protection key ring (persisted in production) and a clock.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ReceivedReference>();

// Meta tags, robots.txt and the canonical-host redirect; Seo:BaseUrl comes from the public address (D-045).
builder.Services.AddPortalSeo(builder.Configuration);
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

// Forwarded headers, correlation id and security headers first (with the ticket pages' own header rules, which win over the shared ones), then the canonical-host redirect, then the
// plain error page and the not-found page (only 404 is re-executed).
app.UseTechStrapWebHost(PortalHeaderRules.Rules);
app.UsePortalSeo();
app.UseTechStrapErrorPages();

// Before the antiforgery check, which reads the form: a post over the endpoint's size limit is a plain 413, not the framework's 400 about a token.
app.UseMiddleware<RequestTooLargeMiddleware>();
app.UseAntiforgery();
app.MapStandardHealthChecks();
app.MapPortalSeo();
app.MapSuggest();
app.MapAttachmentPassThrough();
app.MapRazorComponentsWithStaticAssets<App>();

app.Run();

namespace TechStrap.Portal
{
    public partial class Program;
}
