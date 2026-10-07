using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace TechStrap.Client;

/// <summary>Registers the TechStrap client.</summary>
public static class TechStrapClientServiceCollectionExtensions
{
    /// <summary>Registers the client and configures its options with <paramref name="configure"/>. Options are validated on first use, not at start.</summary>
    public static IServiceCollection AddTechStrapClient(this IServiceCollection services, Action<TechStrapClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        return AddCore(services);
    }

    /// <summary>
    /// Registers the client and binds its options from <paramref name="configuration"/>, which is the section that holds the six keys (for example
    /// <c>configuration.GetSection(TechStrapClientDefaults.ConfigurationSection)</c>). A missing key keeps its default.
    /// </summary>
    public static IServiceCollection AddTechStrapClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TechStrapClientOptions>(options => Bind(configuration, options));
        return AddCore(services);
    }

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        // A second call must not add a second handler or a second named-client configuration.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TechStrapClientMarker)))
        {
            return services;
        }

        services.AddSingleton<TechStrapClientMarker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TechStrapClientOptions>, TechStrapClientOptionsValidator>());
        services.TryAddTransient<ApiKeyHandler>();

        services.AddHttpClient(TechStrapClientDefaults.HttpClientName)
            .RemoveAllLoggers()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value.BaseAddress;

                // The resilience pipeline owns the deadline (the total budget in the options), so the HttpClient's own timeout stays off.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<ApiKeyHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // Seam: the typed ITechStrapClient registration and its resilience pipeline are added here by the next task.
        return services;
    }

    private static void Bind(IConfiguration configuration, TechStrapClientOptions options)
    {
        if (configuration[TechStrapClientOptions.BaseAddressKey] is { Length: > 0 } address)
        {
            // An unparsable value stays relative, so the validator reports it by name instead of this binder throwing.
            options.BaseAddress = Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ? uri : new Uri(address.Trim(), UriKind.Relative);
        }

        if (configuration[TechStrapClientOptions.ApiKeyKey] is { } key)
        {
            options.ApiKey = key;
        }

        if (TimeSpan.TryParse(configuration[TechStrapClientOptions.TimeoutKey], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }

        if (int.TryParse(configuration[TechStrapClientOptions.MaxAttemptsKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempts))
        {
            options.MaxAttempts = attempts;
        }

        if (TimeSpan.TryParse(configuration[TechStrapClientOptions.RetryBaseDelayKey], CultureInfo.InvariantCulture, out var baseDelay))
        {
            options.RetryBaseDelay = baseDelay;
        }

        if (TimeSpan.TryParse(configuration[TechStrapClientOptions.MaxRetryDelayKey], CultureInfo.InvariantCulture, out var maxDelay))
        {
            options.MaxRetryDelay = maxDelay;
        }
    }

    /// <summary>Registered once so a second <c>AddTechStrapClient</c> call can tell the client is already wired.</summary>
    private sealed class TechStrapClientMarker;
}
