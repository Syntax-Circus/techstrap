using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Infrastructure.Attachments;

internal sealed class KbImageStore(IStorageProvider storage) : IKbImageStore
{
    private const string Target = "file";

    public async Task<Result<StoredKbImage>> SaveAsync(IncomingKbImage image, CancellationToken cancellationToken)
    {
        var capped = await CappedImageIntake.ReadAsync(image.Content, image.Length, KbLimits.MaxImageBytes, cancellationToken);
        if (capped is null)
        {
            return TooLarge();
        }

        await using var content = capped.Content;
        var extension = capped.Extension;
        if (extension is null)
        {
            return Failure("kb-image-type-not-allowed", "Only PNG, JPEG, GIF and WebP images can be uploaded.");
        }

        var fileName = $"{Guid.CreateVersion7():N}.{extension}";
        var key = KbImageName.StorageKey(fileName);
        var contentType = KbImageName.ContentTypeOf(fileName);
        content.Position = 0;
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

        return Result<StoredKbImage>.Success(new StoredKbImage(key, fileName, contentType, content.Length));
    }

    public async Task<KbImageContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken)
    {
        // Only a name the store itself could have written reaches storage, so no path segment, dot-dot or prefix change can.
        if (!KbImageName.IsValid(fileName))
        {
            return null;
        }

        var result = await storage.ReadAsync(KbImageName.StorageKey(fileName), cancellationToken);
        return result is null ? null : new KbImageContent(new OwnedReadStream(result.Content, result), KbImageName.ContentTypeOf(fileName), result.Content.CanSeek ? result.Content.Length : -1);
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

    private static Result<StoredKbImage> TooLarge() =>
        Failure("kb-image-too-large", $"This image is too large. Images can be up to {KbLimits.MaxImageBytes / (1024 * 1024)} MB.");

    private static Result<StoredKbImage> Failure(string code, string message) =>
        Result<StoredKbImage>.Failure(new ResultError(code, message, ResultErrorKind.Validation, Target));
}
