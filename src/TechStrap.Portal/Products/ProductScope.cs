namespace TechStrap.Portal.Products;

/// <summary>
/// The product the current request is about, set by a product page once it has loaded the product and read by <c>PortalLayout</c> for the header, the footer and the accent. It is a scoped service, so
/// every request has its own: the not-found and error pages, which the host renders in a fresh scope (re-execution), know no product and stay neutral, whatever happened before them.
/// </summary>
public sealed class ProductScope
{
    public ProductThemeViewModel? Theme { get; private set; }

    /// <summary>Raised when a page sets the product after the layout has rendered, so the layout renders again.</summary>
    public event Action? Changed;

    public void Set(ProductThemeViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Changed?.Invoke();
    }
}
