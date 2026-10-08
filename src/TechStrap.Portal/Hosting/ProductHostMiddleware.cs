using Microsoft.Extensions.Options;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Hosting;

/// <summary>
/// Gives a product host its clean paths (PHASE-11e). On a product's own host the clean paths (<c>/</c>, <c>/contact</c>, <c>/kb/...</c>) are rewritten to the product's <c>/p/{key}</c> routes before routing, so the
/// pages are the ones that exist already; the request's <c>PathBase</c> and query are not touched. Paths every host serves (<see cref="PassThroughPrefixes"/>) and paths the table does not know are left as they are.
/// <para>
/// Canonical addresses: a GET or HEAD to <c>/p/{key}/...</c> is answered with a 301 to the product's clean address (its own host, or the public URL when it has none); every other method is rewritten or served, never
/// redirected, because a 301 would drop the body of a post. Redirect targets are built only from a stored product host or from <see cref="PortalOptions.PublicUrl"/>, never from the request's Host header. A host the
/// map does not know behaves exactly like the default host.
/// </para>
/// </summary>
public sealed class ProductHostMiddleware(RequestDelegate next)
{
    /// <summary>The paths every host serves as they are (the ticket pages, the framework and static assets, the SEO files, the health checks and the host's own error pages).</summary>
    public static readonly IReadOnlyList<string> PassThroughPrefixes =
    [
        $"{PortalRoutes.TicketPrefix}/",
        PortalRoutes.FrameworkPrefix,
        PortalRoutes.BlazorPrefix,
        PortalRoutes.ContentPrefix,
        PortalRoutes.CssPrefix,
        PortalRoutes.JsPrefix,
        PortalRoutes.ImgPrefix,
        PortalRoutes.FaviconPrefix,
        PortalRoutes.SitemapPath,
        PortalRoutes.RobotsPath,
        PortalRoutes.HealthPrefix,
        PortalRoutes.NotFoundTemplate,
        PortalRoutes.ErrorTemplate,
        PortalRoutes.StyleGuideTemplate,
    ];

    // The product's pages that have a clean form; the path and everything under it is rewritten (/contact/received is under /contact).
    private static readonly string[] RewrittenRoots =
    [
        PortalRoutes.ContactPath,
        PortalRoutes.LostLinkPath,
        PortalRoutes.KbPath,
        PortalRoutes.SuggestPath,
    ];

    /// <summary>Resolves the request's host, fills the scoped <paramref name="productHost"/>, then rewrites or redirects.</summary>
    public async Task InvokeAsync(HttpContext context, IProductHostResolver resolver, ProductHostContext productHost, ProductHostMap map, IOptions<PortalOptions> options)
    {
        var resolved = await resolver.ResolveAsync(context, context.RequestAborted);
        productHost.Key = resolved.Key;
        productHost.Host = resolved.Host;

        var request = context.Request;
        if (PortalRoutes.TryStripProductPrefix(request.Path, out var key, out var rest))
        {
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && (productHost.IsProductHost || ProductHostMap.CanBeProductHost(request.Host.Host.ToLowerInvariant())))
            {
                await map.EnsureFreshAsync(context.RequestAborted);
                if (CanonicalAddress(key, rest, request.QueryString, productHost, map, options.Value) is { } address)
                {
                    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    context.Response.Headers.Location = address;
                    return;
                }
            }
        }
        else if (productHost.Key is { } productKey && TryRewrite(request.Path, productKey, out var rewritten))
        {
            request.Path = rewritten;
        }

        await next(context);
    }

    private static string? CanonicalAddress(string key, PathString rest, QueryString query, ProductHostContext productHost, ProductHostMap map, PortalOptions portal)
    {
        var path = rest.HasValue ? rest.ToUriComponent() : "/";
        if (productHost.IsProductHost && string.Equals(key, productHost.Key, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{productHost.Host}{path}{query}";
        }

        if (map.TryGetHost(key, out var ownHost))
        {
            return $"https://{ownHost}{path}{query}";
        }

        // Another product without a host, asked for on a product host: its page is on the public URL. With no public URL (Development) there is nowhere to send it, and a relative address would loop on this host.
        return productHost.IsProductHost && portal.PublicBaseUrl.Length > 0
            ? $"{portal.PublicBaseUrl}{PortalRoutes.ProductHome(key)}{rest.ToUriComponent()}{query}"
            : null;
    }

    private static bool TryRewrite(PathString path, string key, out PathString rewritten)
    {
        rewritten = default;
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value == PortalRoutes.HomeTemplate)
        {
            rewritten = new PathString(PortalRoutes.ProductHome(key));
            return true;
        }

        if (PassThroughPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!RewrittenRoots.Any(root => path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        rewritten = new PathString(PortalRoutes.ProductHome(key) + value);
        return true;
    }
}
