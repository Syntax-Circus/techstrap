using SyntaxCircus.Common;

namespace TechStrap.Application.Products;

/// <summary>An uploaded logo as a readable stream. <see cref="Length"/> is the declared length. The file name and the declared content type are deliberately absent: the leading bytes decide the type and the store picks the name.</summary>
public sealed record IncomingProductLogo(long Length, Stream Content);

/// <summary>A stored logo. <see cref="Key"/> is <c>product-logos/{name}</c>; <see cref="FileName"/> is the <c>{guid}.{ext}</c> part, which is also its public address and what the product row holds.</summary>
public sealed record StoredProductLogo(string Key, string FileName, string ContentType, long Size);

/// <summary>A stored logo opened for reading. The caller disposes <see cref="Content"/>.</summary>
public sealed record ProductLogoContent(Stream Content, string ContentType, long Size);

/// <summary>
/// Uploaded product logos over SyntaxCircus.Storage (D-052). Separate from <c>IAttachmentStore</c> and <c>IKbImageStore</c> because the <c>product-logos/</c> prefix is publicly readable and
/// ticket attachments never live under it. A logo is stored under a random key and has no database row of its own: the product row holds the file name.
/// </summary>
public interface IProductLogoStore
{
    /// <summary>Validation errors (target "file"): <c>product-logo-too-large</c> over 1 MiB, <c>product-logo-type-not-allowed</c> for anything that is not a png, jpeg or webp by its leading bytes (SVG and GIF included).</summary>
    Task<Result<StoredProductLogo>> SaveAsync(IncomingProductLogo logo, CancellationToken cancellationToken);

    /// <summary>Opens <c>product-logos/{fileName}</c>, or returns null when the name is not a well-formed logo name or the object is missing. Nothing outside the prefix can be reached.</summary>
    Task<ProductLogoContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Best effort: deletes the object, swallows storage errors and ignores a name the store could not have written.</summary>
    Task DeleteAsync(string fileName, CancellationToken cancellationToken);
}
