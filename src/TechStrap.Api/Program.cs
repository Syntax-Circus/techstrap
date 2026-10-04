using Microsoft.AspNetCore.RateLimiting;
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Security;
using TechStrap.Infrastructure.Seeding;
using TechStrap.Infrastructure.Tickets;

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
        options.AddEventProcessor(new SensitiveHeaderSentryProcessor());
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapSecurity();
builder.Services.AddTechStrapDevelopmentSeeding();

// Forwarded headers: trust X-Forwarded-* only from the configured proxies and networks, and fail
// startup outside Development when none are configured (CLIENT_IP_RATE_LIMITING.md).
builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);

builder.Services.AddOptions<PublicRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(PublicRateLimitOptions.SectionName))
    .Validate(o => o.PermitLimit >= 1, "RateLimiting:Public:PermitLimit must be >= 1.")
    .Validate(o => o.WindowSeconds >= 1, "RateLimiting:Public:WindowSeconds must be >= 1.")
    .ValidateOnStart();

builder.Services.AddOptions<IntakeRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(IntakeRateLimitOptions.SectionName))
    .Validate(o => o.WebFormPermitLimit >= 1, "RateLimiting:Intake:WebFormPermitLimit must be >= 1.")
    .Validate(o => o.WebFormWindowSeconds >= 1, "RateLimiting:Intake:WebFormWindowSeconds must be >= 1.")
    .Validate(o => o.PublicKeyPermitLimit >= 1, "RateLimiting:Intake:PublicKeyPermitLimit must be >= 1.")
    .Validate(o => o.PublicKeyWindowSeconds >= 1, "RateLimiting:Intake:PublicKeyWindowSeconds must be >= 1.")
    .Validate(o => o.TrustedKeyPermitLimit >= 1, "RateLimiting:Intake:TrustedKeyPermitLimit must be >= 1.")
    .Validate(o => o.TrustedKeyWindowSeconds >= 1, "RateLimiting:Intake:TrustedKeyWindowSeconds must be >= 1.")
    .ValidateOnStart();

builder.Services.AddOptions<CustomerRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(CustomerRateLimitOptions.SectionName))
    .Validate(o => o.TokenAccessPermitLimit >= 1, "RateLimiting:Customer:TokenAccessPermitLimit must be >= 1.")
    .Validate(o => o.TokenAccessWindowSeconds >= 1, "RateLimiting:Customer:TokenAccessWindowSeconds must be >= 1.")
    .Validate(o => o.LostLinkPermitLimit >= 1, "RateLimiting:Customer:LostLinkPermitLimit must be >= 1.")
    .Validate(o => o.LostLinkWindowSeconds >= 1, "RateLimiting:Customer:LostLinkWindowSeconds must be >= 1.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    var limits = builder.Configuration.GetSection(PublicRateLimitOptions.SectionName).Get<PublicRateLimitOptions>()
        ?? new PublicRateLimitOptions();
    options.AddPerIpFixedWindow(PublicRateLimitOptions.PolicyName, limits.PermitLimit, TimeSpan.FromSeconds(limits.WindowSeconds));
    var intake = builder.Configuration.GetSection(IntakeRateLimitOptions.SectionName).Get<IntakeRateLimitOptions>()
        ?? new IntakeRateLimitOptions();
    options.AddIntakePolicies(intake);
    var customer = builder.Configuration.GetSection(CustomerRateLimitOptions.SectionName).Get<CustomerRateLimitOptions>()
        ?? new CustomerRateLimitOptions();
    options.AddCustomerPolicies(customer);
    options.UseProblemDetailsRejection();
});

builder.Services.AddAgentAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddProductApiKeyAuthentication(builder.Configuration);
builder.Services.AddTechStrapIntake(builder.Configuration);
builder.Services.AddTechStrapTicketOperations(builder.Configuration);
builder.Services.AddResultProblemDetails();
builder.Services.AddApplicationHandlers();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

await ApiStartupTasks.RunAsync(app.Services, app.Environment, app.Configuration);

app.UseForwardedHeaders();
app.UseCorrelationId();
// Every customer response (200, uniform 404, 429 from the limiter) is uncacheable. Registered before UseRateLimiter so the 429 is covered.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/customer", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            // Keep a stricter value a result already set (the attachment download sends "private, no-store").
            if (!context.Response.Headers.CacheControl.ToString().Contains("no-store", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
    }

    await next();
});
// Must stay before UseSecurityHeaders: it appends to the CSP at response start and start callbacks run last-registered-first.
app.UseAttachmentSandbox();
app.UseSecurityHeaders();
app.UseProblemDetailsExceptionHandling();
app.UseRequestTooLargeProblemDetails();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
        diagnosticContext.Set("CorrelationId", CorrelationContextAccessor.CurrentCorrelationId);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health endpoints are anonymous and not rate limited.
app.MapGroup(string.Empty).AllowAnonymous().MapStandardHealthChecks();

// The OpenAPI document is the one anonymous public surface in PHASE-01, so it carries the public limit.
app.MapOpenApi().AllowAnonymous().RequireRateLimiting(PublicRateLimitOptions.PolicyName);

app.MapControllers();

app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
