using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.DotEnv;
using TechStrap.Hosting.Security;
using TechStrap.Hosting.Wiring;
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

var telemetry = builder.AddTechStrapObservability(ServiceName);

// Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
// shared with the Admin (TechStrap.Hosting).
// The Content-Security-Policy; the Portal has no sign-in, so form-action is this origin only. Loopback logos only in Development.
builder.Services.AddTechStrapWebHost(builder.Configuration, TechStrapCsp.ForBlazorApp(allowLoopbackImages: builder.Environment.IsDevelopment()));

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

// Forwarded headers, correlation id and security headers first, then the plain error page and the not-found page (only 404 is re-executed).
app.UseTechStrapWebHost();
app.UseTechStrapErrorPages();
app.UseAntiforgery();
app.MapStandardHealthChecks();
app.MapRazorComponentsWithStaticAssets<App>();

app.Run();

namespace TechStrap.Portal
{
    public partial class Program;
}
