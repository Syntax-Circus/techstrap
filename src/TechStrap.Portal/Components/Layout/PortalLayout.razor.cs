using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Skins;
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
    private PortalLinks Links { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private DefaultPackProvider DefaultPack { get; set; } = default!;

    [Inject]
    private PortalSkinFactory Skins { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    // The deployment's default pack and the skin it gives this request: the product's own overrides on top of it, or the pack alone on a neutral page (every address gets the same one).
    private string _pack = SkinPacks.DefaultKey;
    private ResolvedSkin? _skin;

    private ProductThemeViewModel? Theme => Scope.Theme;

    private string SkipHref => Links.ToFragment(Navigation.Uri, "main", keepQuery: true);

    protected override void OnInitialized()
    {
        Scope.Changed += OnScopeChanged;

        // The first render already carries the product's own look (the default pack is Classic until it is read), so the accent never waits for the API.
        Resolve();
    }

    protected override async Task OnInitializedAsync()
    {
        _pack = await DefaultPack.GetAsync(HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        Resolve();
    }

    public void Dispose() => Scope.Changed -= OnScopeChanged;

    // The page sets the product while it initialises, after this layout first rendered: the skin is resolved again with it.
    private void OnScopeChanged() => _ = InvokeAsync(() =>
    {
        Resolve();
        StateHasChanged();
    });

    private void Resolve() => _skin = Skins.Resolve(_pack, Theme?.Skin, Theme?.Accent);
}
