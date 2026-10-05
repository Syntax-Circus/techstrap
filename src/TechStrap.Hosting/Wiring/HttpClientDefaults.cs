using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Hosting.Wiring;

public static class HttpClientDefaults
{
    /// <summary>
    /// Removes the default logging handlers from every <c>HttpClient</c> the factory creates, in every host (Api, Worker, Admin, Portal). The factory's default
    /// logging writes each raw request header into structured log state at Trace (event id 102): an <c>Authorization</c> header, or an OTLP exporter's
    /// <c>x-api-key</c>. The exporters use the factory too, so the default must be host-wide, not per client. Only the logging handlers go; the auth,
    /// forwarded-IP and resilience handlers are untouched. The leak tests scan every level (Verbose) and prove an export was attempted, so they cannot pass
    /// vacuously.
    /// </summary>
    public static IServiceCollection AddTechStrapHttpClientDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());
        return services;
    }
}
