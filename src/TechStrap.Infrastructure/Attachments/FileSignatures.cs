using System.Text;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>Allowed attachment kinds: canonical content type per extension, checked against the leading bytes.</summary>
internal static class FileSignatures
{
    /// <summary>Number of leading bytes inspected.</summary>
    public const int HeadLength = 8 * 1024;

    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] _gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] _gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] _pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] _zipLocal = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] _zipEmpty = [0x50, 0x4B, 0x05, 0x06];

    // Browser and OS aliases seen in the wild; content sniffing is the real control.
    private static readonly Dictionary<string, string[]> _aliases = new()
    {
        [".zip"] = ["application/x-zip-compressed", "application/zip-compressed"],
        [".csv"] = ["application/vnd.ms-excel", "text/plain"],
        [".jpg"] = ["image/jpg", "image/pjpeg"],
        [".jpeg"] = ["image/jpg", "image/pjpeg"],
        [".log"] = ["text/x-log"],
    };

    public static bool TryMatch(string extension, ReadOnlySpan<byte> head, out string contentType)
    {
        contentType = string.Empty;
        switch (extension.ToLowerInvariant())
        {
            case ".png" when head.StartsWith(_png):
                contentType = "image/png";
                return true;
            case ".jpg" or ".jpeg" when head.StartsWith(_jpeg):
                contentType = "image/jpeg";
                return true;
            case ".gif" when head.StartsWith(_gif87) || head.StartsWith(_gif89):
                contentType = "image/gif";
                return true;
            case ".webp" when head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8):
                contentType = "image/webp";
                return true;
            case ".pdf" when head.StartsWith(_pdf):
                contentType = "application/pdf";
                return true;
            case ".zip" when head.StartsWith(_zipLocal) || head.StartsWith(_zipEmpty):
                contentType = "application/zip";
                return true;
            case ".txt" or ".log" when IsText(head):
                contentType = "text/plain";
                return true;
            case ".csv" when IsText(head):
                contentType = "text/csv";
                return true;
            default:
                return false;
        }
    }

    /// <summary>True when a declared content type is acceptable for the matched kind.</summary>
    public static bool IsDeclaredTypeAcceptable(string extension, string? declared, string canonical)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return true;
        }

        var semicolon = declared.IndexOf(';', StringComparison.Ordinal);
        var type = (semicolon >= 0 ? declared[..semicolon] : declared).Trim();
        if (type.Length == 0
            || type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || type.Equals(canonical, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_aliases.TryGetValue(extension.ToLowerInvariant(), out var aliases) && aliases.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return canonical.StartsWith("text/", StringComparison.Ordinal) && type.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsText(ReadOnlySpan<byte> head)
    {
        if (head.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        // A full-size head may end mid-character, so only a complete file is flushed.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strict.GetDecoder().GetCharCount(head, flush: head.Length < HeadLength);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
