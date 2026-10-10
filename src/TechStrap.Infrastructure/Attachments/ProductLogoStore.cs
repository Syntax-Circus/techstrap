using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;

namespace TechStrap.Infrastructure.Attachments;

internal sealed class ProductLogoStore(IStorageProvider storage) : IProductLogoStore
{
    private const string Target = "file";

    public async Task<Result<StoredProductLogo>> SaveAsync(IncomingProductLogo logo, CancellationToken cancellationToken)
    {
        var capped = await CappedImageIntake.ReadAsync(logo.Content, logo.Length, ProductLogoLimits.MaxBytes, cancellationToken);
        if (capped is null)
        {
            return Failure("product-logo-too-large", $"This image is too large. Logos can be up to {ProductLogoLimits.MaxBytes / (1024 * 1024)} MB.");
        }

        await using var content = capped.Content;
        var extension = capped.Extension;
        var fileName = extension is null ? null : $"{Guid.CreateVersion7():N}.{extension}";
        if (fileName is null || !ProductLogoName.IsValid(fileName))
        {
            return Failure("product-logo-type-not-allowed", "Only PNG, JPEG and WebP images can be uploaded.");
        }

        var key = ProductLogoName.StorageKey(fileName);
        var contentType = ProductLogoName.ContentTypeOf(fileName);
        try
        {
            await storage.StoreAsync(new StoreObjectRequest(key, content, contentType), cancellationToken);
        }
        catch
        {
            // A failed copy can leave a partial object behind.
            await DeleteQuietlyAsync(key);
            throw;
        }

        return Result<StoredProductLogo>.Success(new StoredProductLogo(key, fileName, contentType, content.Length));
    }

    public async Task<ProductLogoContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken)
    {
        // Only a name the store itself could have written reaches storage, so no path segment, dot-dot or prefix change can.
        if (!ProductLogoName.IsValid(fileName))
        {
            return null;
        }

        var result = await storage.ReadAsync(ProductLogoName.StorageKey(fileName), cancellationToken);
        return result is null ? null : new ProductLogoContent(new OwnedReadStream(result.Content, result), ProductLogoName.ContentTypeOf(fileName), result.Content.CanSeek ? result.Content.Length : -1);
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!ProductLogoName.IsValid(fileName))
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(ProductLogoName.StorageKey(fileName), cancellationToken);
        }
        catch (Exception)
        {
            // Best effort: a leftover file is harmless, and the caller has already committed.
        }
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best effort only; the original failure is the one to surface.
        }
    }

    private static Result<StoredProductLogo> Failure(string code, string message) =>
        Result<StoredProductLogo>.Failure(new ResultError(code, message, ResultErrorKind.Validation, Target));
}
