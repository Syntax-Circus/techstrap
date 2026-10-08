using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Products;

internal static class ProductErrors
{
    public static ResultError NotFound() => new("product-not-found", "That product does not exist.", ResultErrorKind.NotFound);

    public static ResultError HostInvalid() =>
        new("product-host-invalid", "Use a hostname such as support.example.com: letters, digits and hyphens, no scheme, port or path.", ResultErrorKind.Validation, "portalHost");

    public static ResultError HostTaken() =>
        new("product-host-taken", "Another product already uses this portal hostname.", ResultErrorKind.Conflict);

    public static ResultError KeyTaken() =>
        new("product-key-taken", "Another product already uses this key or ticket number prefix. Choose different ones.", ResultErrorKind.Conflict);

    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "This product changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
}
