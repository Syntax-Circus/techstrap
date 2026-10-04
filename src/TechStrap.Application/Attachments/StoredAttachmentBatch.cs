using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Results;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Attachments;

/// <summary>
/// The files one reply has stored so far, with the compensation rule shared by every reply handler (06a): files saved before a failure
/// are deleted, and once the commit succeeds the caller calls <see cref="Keep"/> so nothing afterwards can delete them.
/// </summary>
internal sealed class StoredAttachmentBatch(IAttachmentStore store, ILogger logger)
{
    private readonly List<string> _keys = [];

    /// <summary>Saves each file against the ticket and links it to the message. On any failure the files stored so far are deleted and the error is returned.</summary>
    public async Task<ResultError?> SaveAllAsync(
        Guid ticketId, Message message, IReadOnlyList<IncomingAttachment> files, TimeProvider clock, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            var saved = await store.SaveAsync(ticketId, file, cancellationToken);
            if (saved.IsFailure)
            {
                await DeleteAllAsync();
                return saved.Errors[0];
            }

            _keys.Add(saved.Value.StorageKey);
            var added = message.AddAttachment(saved.Value.FileName, saved.Value.ContentType, saved.Value.Size, saved.Value.StorageKey, clock);
            if (added.IsFailure)
            {
                await DeleteAllAsync();
                return added.Error!.ToError();
            }
        }

        return null;
    }

    /// <summary>The commit succeeded: committed rows reference the files, so they must not be deleted any more.</summary>
    public void Keep() => _keys.Clear();

    /// <summary>Best-effort delete of every stored file, ignoring the request's cancellation.</summary>
    public async Task DeleteAllAsync()
    {
        await StoredFileCleanup.DeleteAllAsync(store, _keys, logger);
        _keys.Clear();
    }
}
