using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;
using Sentry.AspNetCore;
using TechStrap.Hosting.Sentry;
using TechStrap.Hosting.Wiring;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The shared host wiring in TechStrap.Hosting (PHASE-07c): every host drops the default <c>HttpClient</c> logging, and the two browser hosts get the same redaction
/// and Sentry scrubbing, so the Portal no longer lags the Admin.
/// </summary>
public sealed class HostWiringTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters

    /// <summary>The handlers the factory puts in front of the transport for any client name, outermost first.</summary>
    private static IReadOnlyList<string> HandlerChain(IServiceProvider services)
    {
        var names = new List<string>();
        for (HttpMessageHandler? handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("any-client-at-all");
             handler is not null;
             handler = (handler as DelegatingHandler)?.InnerHandler)
        {
            names.Add(handler.GetType().Name);
        }

        return names;
    }

    private static bool LoggingSuppressed(IServiceProvider services) =>
        !HandlerChain(services).Any(name => name.StartsWith("Logging", StringComparison.Ordinal));

    [Fact]
    public async Task The_Api_drops_the_default_HttpClient_logging()
    {
        await using var factory = new ApiFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Worker_drops_the_default_HttpClient_logging()
    {
        await using var factory = new WorkerFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Admin_drops_the_default_HttpClient_logging()
    {
        await using var factory = new AdminFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Portal_drops_the_default_HttpClient_logging()
    {
        await using var factory = new PortalFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public void A_host_that_does_not_call_the_default_keeps_the_factory_logging_so_the_check_above_can_fail()
    {
        var services = new ServiceCollection().AddLogging().AddHttpClient("probe").Services.BuildServiceProvider();

        HandlerChain(services).ShouldContain("LoggingHttpMessageHandler");
        LoggingSuppressed(services).ShouldBeFalse();
    }

    [Fact]
    public async Task The_Portal_redacts_what_application_code_logs()
    {
        await using var factory = new PortalFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token}", "ada@example.com", Token);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\"");
    }

    [Fact]
    public void The_shared_observability_wiring_scrubs_credential_headers_from_Sentry_events_and_transactions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sentry:Dsn"] = "https://key@example.invalid/1" });

        builder.AddTechStrapObservability("techstrap-wiring-test");

        using var app = builder.Build();
        var sentry = app.Services.GetRequiredService<IOptions<SentryAspNetCoreOptions>>().Value;
        sentry.GetAllEventProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        sentry.GetAllTransactionProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        sentry.AutoSessionTracking.ShouldBeFalse();
    }
}
