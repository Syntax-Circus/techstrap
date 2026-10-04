using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechStrap.Admin.Features.Shell;

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the per-circuit shell services. Scoped, so each circuit has its own message slot and its own key listener.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StatusMessageService>();
        services.AddScoped<ShortcutService>();
        return services;
    }
}
