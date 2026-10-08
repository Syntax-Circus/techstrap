using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    /// <summary>A product with every branding field settable, for the editor tests (<see cref="Product"/> has fixed branding and version 1).</summary>
    public static ProductDto ProductDetail(
        string name = "Orbitly",
        Guid? id = null,
        bool active = true,
        uint version = 7,
        string? logo = null,
        string accent = "#1D4ED8",
        string? from = null,
        string? replyTo = null,
        string key = "orbitly",
        string prefix = "ORB",
        string? portalHost = null) => new(
            id ?? OrbitlyId, key, name, prefix, active,
            new ProductBrandingDto(name, logo, accent, "#FFFFFF", accent, from, replyTo), version, portalHost);
}
