using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Caching;

/// <summary>
/// The framework's output cache for the help centre (D-045 addendum, PHASE-09c): one base policy, not an attribute on a page, so the rule for what is kept is in one place (<see cref="PortalCachePaths"/>) and a form or
/// ticket page can never be kept by forgetting to leave something off. Kept for <see cref="PortalCachePaths.Lifetime"/>, varying by the <c>page</c> query value only and not by host: the framework's default key holds the whole
/// query string and the host, so a visitor could fill the store with <c>?utm=1</c>, <c>?utm=2</c> and so on, and the page does not depend on either (the canonical address comes from <c>Seo:BaseUrl</c>, never the Host header).
/// </summary>
internal static class PortalOutputCache
{
    public static IServiceCollection AddPortalOutputCache(this IServiceCollection services)
    {
        services.AddOutputCache(options => options.AddBasePolicy(policy => policy
            .With(context => PortalCachePaths.IsCacheable(context.HttpContext.Request))
            .Expire(PortalCachePaths.Lifetime)
            .SetVaryByQuery(PortalRoutes.PageParameter)
            .SetVaryByHost(false)));
        return services;
    }

    /// <summary>
    /// Place it after the error pages and before the endpoints. The shared security headers and the per-path rules are applied by steps that sit before it, when the response starts, so they are right on a cached answer
    /// too. One thing is not: the correlation id middleware puts the request's id on the response before the cache sees it, so the cache stores the first request's id and replays it on every hit (the spike sent
    /// <c>cid-two</c> and got <c>cid-one</c>). The step below sets the request's own id again when the response starts, which is after the cache has written a stored copy.
    /// </summary>
    public static WebApplication UsePortalOutputCache(this WebApplication app)
    {
        var headerName = app.Services.GetRequiredService<IOptions<CorrelationIdOptions>>().Value.HeaderName;
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.Items[headerName] is string correlationId)
                {
                    context.Response.Headers[headerName] = correlationId;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
        app.UseOutputCache();
        return app;
    }
}
