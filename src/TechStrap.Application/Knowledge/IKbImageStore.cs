using System.Text.RegularExpressions;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

/// <summary>An uploaded image as a readable stream. <see cref="Length"/> is the declared length. The file name and the declared content type are deliberately absent: the leading bytes decide the type and the store picks the name.</summary>
public sealed record IncomingKbImage(long Length, Stream Content);

/// <summary>A stored image. <see cref="Key"/> is <c>kb-images/{name}</c>; <see cref="FileName"/> is the <c>{guid}.{ext}</c> part, which is also its public address.</summary>
public sealed record StoredKbImage(string Key, string FileName, string ContentType, long Size);

/// <summary>A stored image opened for reading. The caller disposes <see cref="Content"/>.</summary>
public sealed record KbImageContent(Stream Content, string ContentType, long Size);

/// <summary>
/// KB images over SyntaxCircus.Storage (D-021, D-044). Separate from <c>IAttachmentStore</c> because the <c>kb-images/</c> prefix is publicly readable and
/// ticket attachments never live under it. An image is stored under a random key and has no database row.
/// </summary>
public interface IKbImageStore
{
    /// <summary>Validation errors (target "file"): <c>kb-image-too-large</c> over 5 MB, <c>kb-image-type-not-allowed</c> for anything that is not a png, jpeg, gif or webp by its leading bytes (SVG included), or that carries markup.</summary>
    Task<Result<StoredKbImage>> SaveAsync(IncomingKbImage image, CancellationToken cancellationToken);

    /// <summary>Opens <c>kb-images/{fileName}</c>, or returns null when the name is not a well-formed image name or the object is missing. Nothing outside the prefix can be reached.</summary>
    Task<KbImageContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>The public address of a stored image: <c>{TECHSTRAP_API_PUBLIC_URL or the request origin}/kb-images/{fileName}</c>.</summary>
public interface IKbImageUrls
{
    string UrlFor(string fileName);
}

/// <summary>The only image names the store writes and serves: 32 lower-case hex digits, a dot and one of four extensions. Anything else, including any path segment, is refused before storage is touched.</summary>
public static partial class KbImageName
{
    public const string Png = "png";
    public const string Jpeg = "jpg";
    public const string Gif = "gif";
    public const string Webp = "webp";

    // \z, not $: $ would also accept a trailing newline.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|gif|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static string StorageKey(string fileName) => KbLimits.ImagePathPrefix + fileName;

    /// <summary>The content type for a valid name's extension. Only call it for names <see cref="IsValid"/> accepts.</summary>
    public static string ContentTypeOf(string fileName) => fileName[(fileName.LastIndexOf('.') + 1)..] switch
    {
        Png => "image/png",
        Jpeg => "image/jpeg",
        Gif => "image/gif",
        Webp => "image/webp",
        _ => throw new ArgumentException("Not a KB image name.", nameof(fileName)),
    };
}
