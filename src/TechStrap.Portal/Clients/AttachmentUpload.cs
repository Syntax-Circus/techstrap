namespace TechStrap.Portal.Clients;

/// <summary>
/// One file to send on with a new ticket or a reply. <see cref="OpenRead"/> is called when the multipart body is built (never before the product key or token has been checked), and the stream it returns is
/// owned by that body: it is closed when the request is disposed, so the file is read once, as it is sent, and is never held in memory by the Portal.
/// </summary>
public sealed record AttachmentUpload(string FileName, string? ContentType, Func<Stream> OpenRead);
