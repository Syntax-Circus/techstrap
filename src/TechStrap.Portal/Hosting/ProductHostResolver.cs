using Microsoft.Extensions.Options;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Hosting;

/// <summary>
/// Looks the request's host up in the <see cref="ProductHostMap"/>. The comparison is ordinal on the lower-cased host without its port. The default host (the host of <see cref="PortalOptions.PublicUrl"/>) is never looked
/// up, and neither is a host that can never be a product host (<see cref="ProductHostMap.CanBeProductHost"/>), so those cost no API call; a host that is none of these is looked up, and a miss may refresh the map (at most once in ten seconds). The result carries the host as it is stored, never the request's text.
/// </summary>
public sealed class ProductHostResolver(ProductHostMap map, IOptions<PortalOptions> options) : IProductHostResolver
{
    /// <inheritdoc />
    public async ValueTask<ProductHostContext> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var resolved = new ProductHostContext();
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
        {
            return resolved;
        }

        host = host.ToLowerInvariant();
        if (!ProductHostMap.CanBeProductHost(host) || string.Equals(host, DefaultHost(options.Value), StringComparison.Ordinal))
        {
            return resolved;
        }

        var key = await map.FindKeyAsync(host, refreshOnMiss: true, cancellationToken);
        if (key is not null && map.TryGetHost(key, out var stored))
        {
            resolved.Key = key;
            resolved.Host = stored;
        }

        return resolved;
    }

    private static string? DefaultHost(PortalOptions portal) =>
        Uri.TryCreate(portal.PublicBaseUrl, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
}
