using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Storage;

namespace TechStrap.Api.Tests.Support;

/// <summary>
/// Wraps the real storage provider and fails the Nth store with a full-disk <see cref="IOException"/>; every other call goes to the real provider, so earlier files
/// really are on disk when the failure comes.
/// </summary>
public sealed class FailingStorageProvider(IStorageProvider inner, int failOnStoreCall) : IStorageProvider
{
    private int _stores;

    public async Task<StoredObject> StoreAsync(StoreObjectRequest request, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _stores) == failOnStoreCall)
        {
            // A real full disk leaves the partial object the failed copy had written; the store's compensating delete must remove it.
            await inner.StoreAsync(new StoreObjectRequest(request.Key, new MemoryStream(new byte[1]), request.ContentType), cancellationToken);
            throw new IOException("No space left on device");
        }

        return await inner.StoreAsync(request, cancellationToken);
    }

    public Task<StorageReadResult?> ReadAsync(string key, CancellationToken cancellationToken = default) => inner.ReadAsync(key, cancellationToken);

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);

    /// <summary>Replaces the last <see cref="IStorageProvider"/> registration with one that fails the <paramref name="failOnStoreCall"/>th store.</summary>
    public static void Register(IServiceCollection services, int failOnStoreCall)
    {
        var real = services.Last(descriptor => descriptor.ServiceType == typeof(IStorageProvider));
        services.Remove(real);
        services.AddSingleton<IStorageProvider>(provider => new FailingStorageProvider(
            (IStorageProvider)(real.ImplementationInstance ?? real.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)),
            failOnStoreCall));
    }
}
