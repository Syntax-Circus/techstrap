using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechStrap.Portal.Hosting;

/// <summary>Registers and uses the product hosts (PHASE-11e).</summary>
public static class ProductHostRegistration
{
    /// <summary>The host map (one for the process), its resolver and the per-request <see cref="ProductHostContext"/>. The clock is the system's unless a test registers its own.</summary>
    public static IServiceCollection AddProductHosts(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ProductHostMap>();
        services.AddSingleton<IProductHostResolver, ProductHostResolver>();
        services.AddScoped<ProductHostContext>();
        return services;
    }

    /// <summary>
    /// The product-host middleware, then routing. Routing is explicit because the rewrite must happen first: the framework adds routing at the very start of the pipeline when the application does not call it, which
    /// would pick the endpoint for the clean path (and find none).
    /// </summary>
    public static WebApplication UseProductHosts(this WebApplication app)
    {
        app.UseMiddleware<ProductHostMiddleware>();
        app.UseRouting();
        return app;
    }
}
