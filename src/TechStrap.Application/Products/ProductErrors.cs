using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Products;

internal static class ProductErrors
{
    public static ResultError NotFound() => new("product-not-found", "That product does not exist.", ResultErrorKind.NotFound);

    public static ResultError HostInvalid() =>
        new("product-host-invalid", "Use a hostname such as support.example.com: letters, digits and hyphens, no scheme, port or path.", ResultErrorKind.Validation, "portal-host");

    public static ResultError HostReserved() =>
        new("product-host-reserved", "This is the portal's own address. Use a hostname of the product's own, such as support.example.com.", ResultErrorKind.Validation, "portal-host");

    public static ResultError HostTaken() =>
        new("product-host-taken", "Another product already uses this portal hostname.", ResultErrorKind.Conflict);

    public static ResultError KeyTaken() =>
        new("product-key-taken", "Another product already uses this key or ticket number prefix. Choose different ones.", ResultErrorKind.Conflict);

    public static ResultError LogoFileRequired() =>
        new("file-required", "Choose an image to upload.", ResultErrorKind.Validation, "file");

    public static ResultError SkinTooLong() =>
        new("skin-too-long", "The skin is too long to store. Remove some of its overrides.", ResultErrorKind.Validation, "skin");

    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "This product changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
}
