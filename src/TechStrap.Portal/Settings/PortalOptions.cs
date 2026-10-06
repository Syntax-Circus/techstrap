namespace TechStrap.Portal.Settings;

/// <summary>
/// What the Portal needs to know about its surroundings (D-043, D-045). The keys are flat or sectioned exactly as the other hosts spell them, so one env file can configure the Api, the Admin and the
/// Portal. Every value is validated when the host starts (<see cref="PortalOptionsValidator"/>), so a missing or malformed one stops the start with the key named.
/// </summary>
public sealed class PortalOptions
{
    /// <summary>The address of the TechStrap API as the Portal's container reaches it (<c>API__BASEURL</c>; the compose files set <c>http://api/</c>). Required.</summary>
    public const string ApiBaseUrlKey = "Api:BaseUrl";

    /// <summary>The Portal's own public address as customers see it (the same key and value as in the Api). Required outside Development. It is also the base of every canonical URL and of the sitemap (<c>Seo:BaseUrl</c> is derived from it, D-045).</summary>
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    /// <summary>The product the Portal's root page sends visitors to. Optional: blank shows a neutral page with no product list.</summary>
    public const string DefaultProductKey = "TECHSTRAP_PORTAL_DEFAULT_PRODUCT";

    public string ApiBaseUrl { get; set; } = string.Empty;

    public string PublicUrl { get; set; } = string.Empty;

    public string? DefaultProduct { get; set; }

    /// <summary>The API address with a trailing slash, so a relative request path is resolved under it. Call only after validation.</summary>
    public Uri ApiBaseUri => new(ApiBaseUrl.Trim().EndsWith('/') ? ApiBaseUrl.Trim() : ApiBaseUrl.Trim() + "/", UriKind.Absolute);

    /// <summary>The public address without a trailing slash (empty when blank), ready to put in front of a path that starts with a slash.</summary>
    public string PublicBaseUrl => PublicUrl.Trim().TrimEnd('/');

    /// <summary>The default product key, or null when none is configured (blank counts as none).</summary>
    public string? DefaultProductKeyOrNull => string.IsNullOrWhiteSpace(DefaultProduct) ? null : DefaultProduct.Trim();
}
