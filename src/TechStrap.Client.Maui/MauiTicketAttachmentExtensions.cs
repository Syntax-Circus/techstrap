using Microsoft.Maui.Storage;

namespace TechStrap.Client.Maui;

/// <summary>Adapts app-selected MAUI files without picking files or capturing screenshots.</summary>
public static class MauiTicketAttachmentExtensions
{
    /// <summary>Creates a reopenable attachment from a selection. Keep the file accessible and unchanged until retries finish.</summary>
    /// <param name="file">The app's selected file.</param>
    /// <param name="contentType">Optional explicit MIME type; required if the selection has no content type.</param>
    public static TicketAttachment ToTicketAttachment(this FileResult file, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        var type = contentType ?? file.ContentType;
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new TicketAttachment(file.FileName, type, async ct =>
        {
            ct.ThrowIfCancellationRequested();
            // Essentials has no cancellation overload. Release a late result if cancellation
            // arrived while the platform opened it, rather than abandoning its stream.
            var stream = await file.OpenReadAsync();
            if (ct.IsCancellationRequested)
            {
                await stream.DisposeAsync();
                ct.ThrowIfCancellationRequested();
            }
            return stream;
        });
    }
}
