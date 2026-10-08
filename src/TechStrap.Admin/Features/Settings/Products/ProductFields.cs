using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The limits the server enforces on a product, and the order of the fields on the form (so the first error is the first one the agent meets). The field names of a 400 are <see cref="ApiFields"/>.</summary>
public static class ProductFields
{
    public const int KeyMaxLength = 40;
    public const int NameMaxLength = 100;
    public const int DisplayNameMaxLength = 100;
    public const int EmailMaxLength = 320;

    /// <summary>The fields that have an input only while a product is being created (they are read-only text afterwards).</summary>
    public static readonly IReadOnlyList<string> CreateOnly = [ApiFields.Key, ApiFields.NumberPrefix];

    public static readonly IReadOnlyList<string> All =
        [ApiFields.Key, ApiFields.Name, ApiFields.NumberPrefix, ApiFields.DisplayName, ApiFields.LogoPath, ApiFields.AccentColour, ApiFields.FromAddress, ApiFields.ReplyTo, ApiFields.PortalHost];
}
