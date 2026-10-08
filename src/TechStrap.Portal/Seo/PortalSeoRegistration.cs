using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// <c>SyntaxCircus.Blazor.Seo</c> in the Portal (D-045). <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c>, always, so there is one setting for one value and a stray
/// <c>Seo__BaseUrl</c> can never disagree with it. robots.txt disallows the ticket pages and names the sitemap, which lists every active product's help centre (PHASE-09c).
/// </summary>
public static class PortalSeoRegistration
{
    /// <summary>The robots.txt line that keeps crawlers away from every ticket page (the token is in the address).</summary>
    public const string DisallowTickets = $"Disallow: {PortalRoutes.TicketPrefix}/";

    public static IServiceCollection AddPortalSeo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSyntaxCircusSeo(configuration);
        // Host-aware URLs (D-050 amendment): the package's builder stays the default-host path; the decorator follows ProductHostContext. Registered after AddSyntaxCircusSeo, so it wins by last registration on 0.1.4 (AddScoped) and on 0.1.5 (TryAddScoped adds the package builder first).
        services.AddScoped<SeoUrlBuilder>();
        services.AddScoped<ISeoUrlBuilder, ProductHostSeoUrlBuilder>();
        services.AddOptions<SeoOptions>().PostConfigure<IOptions<PortalOptions>>((seo, portal) => seo.BaseUrl = portal.Value.PublicBaseUrl);
        services.AddPortalSitemap();
        return services;
    }

    /// <summary>The canonical-host redirect (an allow-list; nothing happens until <c>CanonicalHost:CanonicalHost</c> and <c>LegacyHosts</c> are set) and the search-indexing headers.</summary>
    public static WebApplication UsePortalSeo(this WebApplication app)
    {
        app.UseSyntaxCircusSeo();
        return app;
    }

    /// <summary>robots.txt and the sitemap (<c>/sitemap.xml</c>, which robots.txt names): the static entries, then the products and articles from the cache that <see cref="PortalSitemap"/> feeds.</summary>
    public static WebApplication MapPortalSeo(this WebApplication app)
    {
        app.MapSeoRobotsTxt(extraDirectives: [DisallowTickets]);
        // The static entries (the default host's root page) come from the provider, so a product host's sitemap does not list an address on another host.
        app.MapSeoSitemap([], PortalSitemap.ProviderAsync, PortalSitemap.ClientCacheDuration);
        return app;
    }
}
