using TechStrap.Domain.Products;

namespace TechStrap.Application.Persistence;

/// <summary>
/// Products and their API keys. <c>Update</c> methods require the record to have been loaded in the current scope. <c>Update(Product)</c>
/// checks the version the Domain object carries (the one the caller originally saw), so a copy loaded in an earlier request conflicts if the
/// row changed since. <see cref="ListAsync"/> results are untracked and cannot be passed to <c>Update</c>; reload with <c>GetByIdAsync</c> first.
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Product?> GetByKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<Product>> ListAsync(bool activeOnly, CancellationToken cancellationToken);

    /// <summary>True when a product other than <paramref name="exceptProductId"/> (when given) already has <paramref name="host"/> as its portal hostname; <paramref name="host"/> is the normalised form.</summary>
    Task<bool> IsPortalHostTakenAsync(string host, Guid? exceptProductId, CancellationToken cancellationToken);

    void Add(Product product);

    void Update(Product product);

    Task<ProductApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductApiKey?> GetApiKeyByHashAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>Newest first, including revoked keys.</summary>
    Task<IReadOnlyList<ProductApiKey>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken);

    void AddApiKey(ProductApiKey key);

    void UpdateApiKey(ProductApiKey key);
}
