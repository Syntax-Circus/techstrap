using System.Globalization;

namespace TechStrap.Portal.Clients;

/// <summary>
/// Makes a browser-supplied file name safe to put in a multipart part (the Portal's own copy of the Admin's rule). <c>MultipartFormDataContent.Add</c> throws on an empty name and <see cref="ApiConnection"/>
/// does not map an <see cref="ArgumentException"/>, so an odd name must never reach it. Some browsers send the whole path, so the name is first cut to its last path segment; then quotes, control characters and
/// Unicode format characters (such as the right-to-left override, which makes <c>fdp.exe</c> read as <c>exe.pdf</c>) are removed and the extension is kept. A name that ends up empty, or only dots, becomes
/// <see cref="Fallback"/>.
/// </summary>
internal static class AttachmentFileName
{
    public const string Fallback = "attachment";

    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        var segment = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;
        var cleaned = string.Concat(segment.Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format && c is not ('"' or '\''))).Trim();
        return cleaned.Length == 0 || cleaned.All(c => c == '.') ? Fallback : cleaned;
    }
}
