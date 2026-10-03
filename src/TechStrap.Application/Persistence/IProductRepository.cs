using TechStrap.Domain.Products;

namespace TechStrap.Application.Persistence;

/// <summary>Products and their API keys. <c>Update</c> methods require the item to have been loaded in the same scope.</summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Product?> GetByKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<Product>> ListAsync(bool activeOnly, CancellationToken cancellationToken);

    void Add(Product product);

    void Update(Product product);

    Task<ProductApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductApiKey?> GetApiKeyByHashAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>Newest first, including revoked keys.</summary>
    Task<IReadOnlyList<ProductApiKey>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken);

    void AddApiKey(ProductApiKey key);

    void UpdateApiKey(ProductApiKey key);
}
