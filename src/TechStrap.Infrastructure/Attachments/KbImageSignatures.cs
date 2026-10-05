using System.Text;
using TechStrap.Application.Knowledge;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>
/// What counts as a KB image (D-044): png, jpeg, gif or webp, recognised by the leading bytes and a plausible first structure, never by the file name or the
/// declared type. SVG has no entry, so it is refused. A file that also carries markup a browser could run (a gif that is really a script) is refused too.
/// </summary>
internal static class KbImageSignatures
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] _gif89 = "GIF89a"u8.ToArray();

    // Lower-case, because the scan lowers the content first. A real image has none of these byte runs; a polyglot that wants to be a page does.
    private static readonly byte[][] _markup =
    [
        "<script"u8.ToArray(), "<svg"u8.ToArray(), "<html"u8.ToArray(), "<iframe"u8.ToArray(), "<body"u8.ToArray(), "<!doctype"u8.ToArray(), "<?php"u8.ToArray(),
    ];

    /// <summary>The file name extension (png, jpg, gif or webp) for an accepted image, or null.</summary>
    public static string? Identify(ReadOnlySpan<byte> content)
    {
        var extension = Match(content);
        return extension is not null && !CarriesMarkup(content) ? extension : null;
    }

    private static string? Match(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(_png) && content.Length >= 33 && content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return KbImageName.Png;
        }

        // FF D8 FF, then a marker byte (APPn, DQT, SOFn, COM...).
        if (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF && content[3] >= 0xC0 && content[3] != 0xFF)
        {
            return KbImageName.Jpeg;
        }

        if (content.Length >= 13 && (content.StartsWith(_gif87) || content.StartsWith(_gif89)))
        {
            return KbImageName.Gif;
        }

        if (content.Length >= 20 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (content.Slice(12, 4).SequenceEqual("VP8 "u8) || content.Slice(12, 4).SequenceEqual("VP8L"u8) || content.Slice(12, 4).SequenceEqual("VP8X"u8)))
        {
            return KbImageName.Webp;
        }

        return null;
    }

    private static bool CarriesMarkup(ReadOnlySpan<byte> content)
    {
        // Lowered byte by byte: Ascii.ToLower stops at the first byte above 0x7F, and every png starts with one.
        var lower = new byte[content.Length];
        for (var i = 0; i < content.Length; i++)
        {
            lower[i] = content[i] is >= (byte)'A' and <= (byte)'Z' ? (byte)(content[i] | 0x20) : content[i];
        }

        foreach (var marker in _markup)
        {
            if (lower.AsSpan().IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
