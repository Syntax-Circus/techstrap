using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Layout;

/// <summary>
/// The Portal's page frame. A page under <c>/p/{key}</c> puts its product in the <see cref="ProductScope"/>, and the layout then wraps it in that product's accent, header and footer; with no product
/// in scope (the root page, not-found, error) the layout is neutral and carries only the "Powered by TechStrap" line.
/// </summary>
public partial class PortalLayout : LayoutComponentBase, IDisposable
{
    [Inject]
    private ProductScope Scope { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private ProductThemeViewModel? Theme => Scope.Theme;

    private string SkipHref => PageLinks.ToFragment(Navigation.Uri, "main", keepQuery: true);

    protected override void OnInitialized() => Scope.Changed += OnScopeChanged;

    public void Dispose() => Scope.Changed -= OnScopeChanged;

    // The page sets the product while it initialises, after this layout first rendered.
    private void OnScopeChanged() => _ = InvokeAsync(StateHasChanged);
}
