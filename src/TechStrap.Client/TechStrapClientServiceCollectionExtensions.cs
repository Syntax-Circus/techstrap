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

        // A dry run first: a value that is present but cannot be read is a mistake to report now, naming the key, not a reason to quietly keep the default.
        var unreadable = new List<string>();
        Bind(configuration, new TechStrapClientOptions(), unreadable);
        if (unreadable.Count > 0)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(TechStrapClientOptions), unreadable);
        }

        services.Configure<TechStrapClientOptions>(options => Bind(configuration, options, null));
        return AddCore(services);
    }

    private const string TimeSpanFormat = "a time span such as 00:00:30";

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
                client.MaxResponseContentBufferSize = TechStrapClientDefaults.MaxResponseBytes;
            })
            .AddHttpMessageHandler<ApiKeyHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // One client for the process: it owns the resilience pipeline, so the circuit breaker is shared by every caller.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITechStrapClient, TechStrapClient>();
        return services;
    }

    /// <summary>Copies the keys that are present into <paramref name="options"/>. A value that cannot be read adds a line naming the key (never the value) to <paramref name="unreadable"/> when it is given.</summary>
    private static void Bind(IConfiguration configuration, TechStrapClientOptions options, List<string>? unreadable)
    {
        if (configuration[TechStrapClientOptions.BaseAddressKey] is { Length: > 0 } address)
        {
            // An unparsable value leaves the address unset, so the validator reports it by name on first use.
            options.BaseAddress = Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ? uri : null;
        }

        if (configuration[TechStrapClientOptions.ApiKeyKey] is { } key)
        {
            options.ApiKey = key;
        }

        if (Read(configuration, TechStrapClientOptions.TimeoutKey, TimeSpanFormat, unreadable) is { } timeout)
        {
            options.Timeout = timeout;
        }

        if (Read(configuration, TechStrapClientOptions.MaxAttemptsKey, "a whole number", unreadable, (string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) is { } attempts)
        {
            options.MaxAttempts = attempts;
        }

        if (Read(configuration, TechStrapClientOptions.RetryBaseDelayKey, TimeSpanFormat, unreadable) is { } baseDelay)
        {
            options.RetryBaseDelay = baseDelay;
        }

        if (Read(configuration, TechStrapClientOptions.MaxRetryDelayKey, TimeSpanFormat, unreadable) is { } maxDelay)
        {
            options.MaxRetryDelay = maxDelay;
        }
    }

    private static TimeSpan? Read(IConfiguration configuration, string name, string expected, List<string>? unreadable) =>
        Read(configuration, name, expected, unreadable, (string text, out TimeSpan value) => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value));

    private static T? Read<T>(IConfiguration configuration, string name, string expected, List<string>? unreadable, TryParse<T> tryParse)
        where T : struct
    {
        if (configuration[name] is not { Length: > 0 } text)
        {
            return null;
        }

        if (tryParse(text.Trim(), out var value))
        {
            return value;
        }

        unreadable?.Add($"{name} must be {expected}.");
        return null;
    }

    private delegate bool TryParse<T>(string text, out T value);

    /// <summary>Registered once so a second <c>AddTechStrapClient</c> call can tell the client is already wired.</summary>
    private sealed class TechStrapClientMarker;
}
