using Microsoft.EntityFrameworkCore;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(TechStrapDbContext context) : IProductRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<ProductRecord>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken))?.ToDomain();

    public async Task<Product?> GetByKeyAsync(string key, CancellationToken cancellationToken) =>
        (await context.Set<ProductRecord>().FirstOrDefaultAsync(p => p.Key == key, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Product>> ListAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var query = context.Set<ProductRecord>().AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        var records = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return [.. records.Select(p => p.ToDomain())];
    }

    public void Add(Product product) => context.Set<ProductRecord>().Add(product.ToRecord());

    public void Update(Product product)
    {
        var record = context.FindLoaded<ProductRecord>(product.Id);
        product.CopyTo(record);
        context.ApplyOriginalVersion(record, product.Version);
    }

    public async Task<ProductApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<ProductApiKeyRecord>().FirstOrDefaultAsync(k => k.Id == id, cancellationToken))?.ToDomain();

    public async Task<ProductApiKey?> GetApiKeyByHashAsync(string keyHash, CancellationToken cancellationToken) =>
        (await context.Set<ProductApiKeyRecord>().FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<ProductApiKey>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken)
    {
        var records = await context.Set<ProductApiKeyRecord>().AsNoTracking()
            .Where(k => k.ProductId == productId)
            .OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id)
            .ToListAsync(cancellationToken);
        return [.. records.Select(k => k.ToDomain())];
    }

    public void AddApiKey(ProductApiKey key) => context.Set<ProductApiKeyRecord>().Add(key.ToRecord());

    public void UpdateApiKey(ProductApiKey key) => key.CopyTo(context.FindLoaded<ProductApiKeyRecord>(key.Id));
}
