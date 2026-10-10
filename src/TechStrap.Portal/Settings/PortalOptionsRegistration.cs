using Microsoft.Extensions.Options;

namespace TechStrap.Portal.Settings;

public static class PortalOptionsRegistration
{
    /// <summary>Binds <see cref="PortalOptions"/> from its four keys and validates it at start (not on first use), so a Portal with a missing API address never starts.</summary>
    public static IServiceCollection AddPortalOptions(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<PortalOptions>, PortalOptionsValidator>();
        services.AddOptions<PortalOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ApiBaseUrl = configuration[PortalOptions.ApiBaseUrlKey]?.Trim() ?? string.Empty;
                options.PublicUrl = configuration[PortalOptions.PublicUrlKey]?.Trim() ?? string.Empty;
                options.DefaultProduct = configuration[PortalOptions.DefaultProductKey]?.Trim();
                options.Landing = configuration[PortalOptions.LandingKey] is { } landing && !string.IsNullOrWhiteSpace(landing) ? landing.Trim() : PortalLandingModes.Neutral;
            })
            .ValidateOnStart();
        return services;
    }
}
