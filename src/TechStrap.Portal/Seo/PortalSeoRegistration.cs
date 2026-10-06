using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// <c>SyntaxCircus.Blazor.Seo</c> in the Portal (D-045). <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c>, always, so there is one setting for one value and a stray
/// <c>Seo__BaseUrl</c> can never disagree with it. robots.txt disallows the ticket pages. The sitemap is not mapped in 09a: it needs the products endpoint PHASE-09c adds.
/// </summary>
public static class PortalSeoRegistration
{
    /// <summary>The robots.txt line that keeps crawlers away from every ticket page (the token is in the address).</summary>
    public const string DisallowTickets = $"Disallow: {PortalRoutes.TicketPrefix}/";

    public static IServiceCollection AddPortalSeo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSyntaxCircusSeo(configuration);
        services.AddOptions<SeoOptions>().PostConfigure<IOptions<PortalOptions>>((seo, portal) => seo.BaseUrl = portal.Value.PublicBaseUrl);
        return services;
    }

    /// <summary>The canonical-host redirect (an allow-list; nothing happens until <c>CanonicalHost:CanonicalHost</c> and <c>LegacyHosts</c> are set) and the search-indexing headers.</summary>
    public static WebApplication UsePortalSeo(this WebApplication app)
    {
        app.UseSyntaxCircusSeo();
        return app;
    }

    /// <summary>robots.txt (and, from 09c, the sitemap).</summary>
    public static WebApplication MapPortalSeo(this WebApplication app)
    {
        app.MapSeoRobotsTxt(extraDirectives: [DisallowTickets]);
        return app;
    }
}
