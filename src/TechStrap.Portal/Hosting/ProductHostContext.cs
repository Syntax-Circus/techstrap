namespace TechStrap.Portal.Hosting;

/// <summary>
/// The product host the current request arrived on, filled by <see cref="ProductHostMiddleware"/> (one per request). <see cref="Key"/> and <see cref="Host"/> are null on the default host and on a host the map
/// does not know. <see cref="Host"/> is the stored product host, never the text of the request's Host header.
/// </summary>
public sealed class ProductHostContext
{
    /// <summary>The key of the product whose host the request arrived on, or null.</summary>
    public string? Key { get; set; }

    /// <summary>The stored host of that product (lower-case, no port), or null.</summary>
    public string? Host { get; set; }

    /// <summary>True when the request arrived on a product host.</summary>
    public bool IsProductHost => Key is not null;
}
