using System.Text.RegularExpressions;

namespace TechStrap.Contracts.Products;

/// <summary>Limits and fixed names of the uploaded product logo (D-052), read by the Api, the Admin pre-check and the Portal.</summary>
public static class ProductLogoLimits
{
    /// <summary>The largest logo an administrator may upload: 1 MiB.</summary>
    public const long MaxBytes = 1L * 1024 * 1024;

    /// <summary>The multipart field that carries the image on <c>POST api/products/{id}/logo</c>.</summary>
    public const string FieldName = "file";

    /// <summary>The public path prefix of an uploaded logo: <c>product-logos/{guid}.{ext}</c>.</summary>
    public const string PathPrefix = "product-logos/";

    /// <summary>The file extensions the Admin's picker offers; the Api decides by the bytes, never by the name.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp"];
}

/// <summary>The only logo names the store writes and serves: 32 lower-case hex digits, a dot and one of three extensions (no GIF, no SVG). Anything else, including any path segment, is refused before storage is touched.</summary>
public static partial class ProductLogoName
{
    /// <summary>The PNG extension, without the dot.</summary>
    public const string Png = "png";
    /// <summary>The JPEG extension, without the dot.</summary>
    public const string Jpeg = "jpg";
    /// <summary>The WebP extension, without the dot.</summary>
    public const string Webp = "webp";

    /// <summary>32 hex digits, a dot and the longest extension (<c>webp</c>).</summary>
    public const int MaxLength = 37;

    // \z, not $: $ would also accept a trailing newline.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>True when <paramref name="name"/> is exactly a name the store writes.</summary>
    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    /// <summary>The storage key (path under the store) of a logo file name.</summary>
    public static string StorageKey(string fileName) => ProductLogoLimits.PathPrefix + fileName;

    /// <summary>The content type for a valid name's extension. Only call it for names <see cref="IsValid"/> accepts.</summary>
    public static string ContentTypeOf(string fileName) => fileName[(fileName.LastIndexOf('.') + 1)..] switch
    {
        Png => "image/png",
        Jpeg => "image/jpeg",
        Webp => "image/webp",
        _ => throw new ArgumentException("Not a product logo name.", nameof(fileName)),
    };
}
