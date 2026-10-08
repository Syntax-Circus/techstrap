namespace TechStrap.Portal.Hosting;

/// <summary>Registers and uses the product hosts (PHASE-11e).</summary>
public static class ProductHostRegistration
{
    /// <summary>The host map (one for the process), its resolver and the per-request <see cref="ProductHostContext"/>. The context is read from the request, not made by the scope: the not-found and error pages are re-executed in a scope of their own and must see the same host. The host map needs the application's TimeProvider (registered by the host).</summary>
    public static IServiceCollection AddProductHosts(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ProductHostMap>();
        services.AddSingleton<IProductHostResolver, ProductHostResolver>();
        services.AddScoped(provider => provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Features.Get<ProductHostContext>() ?? new ProductHostContext());
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
