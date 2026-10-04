namespace TechStrap.Application.Attachments;

/// <summary>An opened attachment for download (application-owned; never a transport type). The receiver disposes Content.</summary>
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long Size);
