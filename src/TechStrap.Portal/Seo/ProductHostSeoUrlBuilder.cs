using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// The package's <see cref="ISeoUrlBuilder"/> made host-aware (D-050 amendment): robots.txt's <c>Sitemap:</c> line, the canonical fallback and the Open Graph image follow the product host the request arrived on. The host
/// is read only from <see cref="ProductHostContext"/> (the stored, lower-case host the middleware resolved), never from the request's Host header; the default host and an unknown host keep the package's behavior.
/// </summary>
internal sealed class ProductHostSeoUrlBuilder(SeoUrlBuilder inner, ProductHostContext host, IOptions<PortalOptions> portal, IHttpContextAccessor accessor) : ISeoUrlBuilder
{
    public string AbsoluteUrl(string? relativeOrAbsolute)
    {
        if (host.IsProductHost)
        {
            var normalised = string.IsNullOrWhiteSpace(relativeOrAbsolute) ? string.Empty
                : relativeOrAbsolute.StartsWith('/') || IsAbsolute(relativeOrAbsolute) ? relativeOrAbsolute : "/" + relativeOrAbsolute;
            return PortalLinks.ComposeAbsolute(host.Host, portal.Value.PublicBaseUrl, normalised);
        }

        return inner.AbsoluteUrl(relativeOrAbsolute);
    }

    public string CanonicalForCurrentRequest(string? overrideRelative)
    {
        if (!string.IsNullOrWhiteSpace(overrideRelative))
        {
            return AbsoluteUrl(overrideRelative);
        }

        if (host.IsProductHost
            && accessor.HttpContext is { } http
            && PortalRoutes.TryStripProductPrefix(http.Request.Path, out _, out var rest))
        {
            return AbsoluteUrl(rest.HasValue ? rest.Value! : string.Empty);
        }

        return inner.CanonicalForCurrentRequest(null);
    }

    private static bool IsAbsolute(string path) =>
        path.StartsWith(PortalLinks.HttpsPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(PortalLinks.HttpPrefix, StringComparison.OrdinalIgnoreCase);
}
