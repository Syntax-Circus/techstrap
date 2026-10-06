namespace TechStrap.Portal.Products;

/// <summary>
/// The product the current request is about, set by a product page once it has loaded the product and read by <c>PortalLayout</c> for the header, the footer and the accent. It is a scoped service, so
/// every request has its own. A 404 that a page ends in <em>after</em> it has set the product (a reply or a post the API then answers 404 to) renders inline in the same scope, with the page's layout, so that
/// page must call <see cref="Clear"/> first; only then is the 404 the neutral page every other 404 is. (The not-found and error pages that the host renders by re-execution get a fresh scope and are neutral anyway.)
/// Clearing at the call site is deliberate: it does not depend on when the response status is set relative to the layout's render.
/// </summary>
public sealed class ProductScope
{
    public ProductThemeViewModel? Theme { get; private set; }

    /// <summary>Raised when a page sets or clears the product after the layout has rendered, so the layout renders again.</summary>
    public event Action? Changed;

    public void Set(ProductThemeViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Changed?.Invoke();
    }

    /// <summary>Forgets the product, so the layout renders neutral again (for a 404 that follows <see cref="Set"/>).</summary>
    public void Clear()
    {
        Theme = null;
        Changed?.Invoke();
    }
}
