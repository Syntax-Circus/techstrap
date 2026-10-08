namespace TechStrap.Portal.Hosting;

/// <summary>Decides which product, if any, a request's Host belongs to.</summary>
public interface IProductHostResolver
{
    /// <summary>The product host of the request: <see cref="ProductHostContext.Key"/> and <see cref="ProductHostContext.Host"/> are null when the host is the default host or one the map does not know.</summary>
    ValueTask<ProductHostContext> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}
