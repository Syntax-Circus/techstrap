namespace TechStrap.Client;

/// <summary>A reopenable attachment. Keep its bytes stable until the submission and any caller retries finish.</summary>
public sealed class TicketAttachment
{
    /// <summary>Creates an attachment source. The factory must return a fresh readable stream at its beginning on each attempt; the SDK disposes it.</summary>
    /// <param name="fileName">The display filename, including its extension; never a local path.</param>
    /// <param name="contentType">The declared MIME type. The server verifies it against the file bytes.</param>
    /// <param name="openReadAsync">Opens a fresh stream. Observe cancellation; streams need not be seekable.</param>
    /// <param name="length">Optional byte length for early validation. Actual bytes are always bounded during upload.</param>
    public TicketAttachment(string fileName, string contentType, Func<CancellationToken, ValueTask<Stream>> openReadAsync, long? length = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(openReadAsync);
        FileName = fileName;
        ContentType = contentType;
        OpenReadAsync = openReadAsync;
        Length = length;
    }

    /// <summary>The filename sent to the server.</summary>
    public string FileName { get; }
    /// <summary>The declared MIME type.</summary>
    public string ContentType { get; }
    /// <summary>The byte length, if known.</summary>
    public long? Length { get; }
    /// <summary>Opens an SDK-owned stream for one attempt.</summary>
    public Func<CancellationToken, ValueTask<Stream>> OpenReadAsync { get; }

    /// <summary>Creates a source that reopens a local file on each attempt. File-access failures become safe submit Result errors.</summary>
    public static TicketAttachment FromFile(string path, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return new TicketAttachment(Path.GetFileName(fullPath), contentType, ct =>
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
        });
    }
}
