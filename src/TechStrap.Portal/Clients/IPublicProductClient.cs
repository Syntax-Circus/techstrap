using SyntaxCircus.Common;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Clients;

/// <summary>The public branding of a product: what every <c>/p/{key}</c> page needs to look like the product's own (D-045).</summary>
public interface IPublicProductClient
{
    /// <summary>
    /// The branding of the active product with this key. A key that is not a slug, an unknown key and an inactive product are all the same not-found error (code <see cref="ApiErrorCodes.NotFound"/>),
    /// and a malformed key is answered without calling the API.
    /// </summary>
    Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken);
}
