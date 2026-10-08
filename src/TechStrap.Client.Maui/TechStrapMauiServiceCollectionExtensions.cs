using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;

namespace TechStrap.Client.Maui;

/// <summary>Registers the MAUI helper.</summary>
public static class TechStrapMauiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MAUI helper on top of a client the app registered with <c>AddTechStrapClient</c>. Resolving <see cref="IMauiTicketSubmitter"/> fails with a clear message when that call is missing.
    /// An Essentials interface the app already registered is kept; otherwise the platform default is used, read only when a value is first asked for.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets what to collect, or <see langword="null"/> for the defaults.</param>
    public static IServiceCollection AddTechStrapMaui(this IServiceCollection services, Action<DeviceContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<DeviceContextOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        // Factories, not instances: registering must not touch the static Essentials accessors, which throw on a platform without the implementation.
        services.TryAddSingleton<IAppInfo>(_ => AppInfo.Current);
        services.TryAddSingleton<IDeviceInfo>(_ => DeviceInfo.Current);
        services.TryAddSingleton<IConnectivity>(_ => Connectivity.Current);
        services.TryAddSingleton<IDeviceDisplay>(_ => DeviceDisplay.Current);
        services.TryAddSingleton<IBattery>(_ => Battery.Default);
        services.TryAddSingleton<IDeviceContextCollector, MauiDeviceContextCollector>();
        services.TryAddSingleton<IMauiTicketSubmitter>(provider => new MauiTicketSubmitter(
            provider.GetService<ITechStrapClient>() ?? throw new InvalidOperationException("Call AddTechStrapClient before AddTechStrapMaui, or use the AddTechStrapMaui overload that takes TechStrapClientOptions."),
            provider.GetRequiredService<IDeviceContextCollector>()));
        return services;
    }

    /// <summary>Registers the client with <paramref name="configureClient"/> and then the MAUI helper.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">Sets the client's options.</param>
    /// <param name="configure">Sets what to collect, or <see langword="null"/> for the defaults.</param>
    public static IServiceCollection AddTechStrapMaui(this IServiceCollection services, Action<TechStrapClientOptions> configureClient, Action<DeviceContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureClient);

        services.AddTechStrapClient(configureClient);
        return services.AddTechStrapMaui(configure);
    }
}
