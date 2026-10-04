using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

internal sealed record ProductRowViewModel(Guid Id, string Name, string Key, string NumberPrefix, bool IsActive, string Accent)
{
    public static ProductRowViewModel From(ProductDto product) =>
        new(product.Id, product.Name, product.Key, product.NumberPrefix, product.IsActive, product.Branding.AccentColour);
}
