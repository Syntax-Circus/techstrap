using Microsoft.Extensions.Logging;

namespace TechStrap.Application.Attachments;

/// <summary>Deletes stored files after a commit (delete ticket, erase requester). Best effort: never throws, ignores cancellation, logs the key and the exception type only.</summary>
internal static class StoredFileCleanup
{
    public static async Task DeleteAllAsync(IAttachmentStore store, IEnumerable<string> storageKeys, ILogger logger)
    {
        foreach (var key in storageKeys)
        {
            try
            {
                await store.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Best effort: an orphaned file must not mask the original outcome, but it must be visible.
                logger.LogWarning("Attachment cleanup failed for {StorageKey} ({ExceptionType}).", key, ex.GetType().Name);
            }
        }
    }
}
