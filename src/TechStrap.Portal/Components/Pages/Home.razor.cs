using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The Portal's root. With <c>TECHSTRAP_PORTAL_DEFAULT_PRODUCT</c> set it sends the visitor to that product's home (a 302; the API is not asked). With <c>TECHSTRAP_PORTAL_LANDING=Products</c> (D-052) it lists the
/// active products whose <c>ListedOnLanding</c> is true as cards; an empty list, a 429 or any API failure renders the neutral copy with 200. Otherwise it is the neutral page of D-045. On a product host the
/// middleware has already rewritten <c>/</c> to that product's home, so the list is never rendered there; the guard below is belt and braces.
/// </summary>
public partial class Home
{
    private IReadOnlyList<LandingCardViewModel> _cards = [];

    [Inject]
    private IOptions<PortalOptions> Portal { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private PortalLinks Links { get; set; } = default!;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductHostContext HostContext { get; set; } = default!;

    [Inject]
    private ProductHostMap HostMap { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    [Inject]
    private DefaultPackProvider DefaultPack { get; set; } = default!;

    [Inject]
    private PortalSkinFactory Skins { get; set; } = default!;

    private bool ShowsCards => _cards.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        if (Portal.Value.DefaultProductKeyOrNull is { } key)
        {
            Navigation.NavigateTo(Links.ProductHome(key));
            return;
        }

        if (!Portal.Value.ListsProducts || HostContext.IsProductHost)
        {
            return;
        }

        var aborted = Http.HttpContext?.RequestAborted ?? CancellationToken.None;
        var listed = await Products.ListAsync(aborted);
        if (listed.IsFailure)
        {
            return;
        }

        // Read once for the whole list; each card is then resolved against it with its own product's overrides and accent.
        var pack = await DefaultPack.GetAsync(aborted);

        _cards = [.. listed.Value
            .Where(product => product.ListedOnLanding)
            .Select(product => new LandingCardViewModel(
                product.Key,
                string.IsNullOrWhiteSpace(product.DisplayName) ? product.Key : product.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(product.Tagline) ? null : product.Tagline.Trim(),
                ProductThemeViewModel.AcceptableLogoUrl(product.LogoUrl, Environment.IsDevelopment()),
                HrefFor(product),
                Skins.Resolve(pack, product.Skin, product.AccentColour)))];
    }

    // A hosted product's card goes to its own host (an absolute https address built from the stored host, never from a header); the others stay on this host under /p/{key}.
    private string HrefFor(PublicProductSummaryDto product)
    {
        var host = string.IsNullOrWhiteSpace(product.PortalHost) ? null : product.PortalHost.Trim().ToLowerInvariant();
        if (host is null)
        {
            return Links.ProductHome(product.Key);
        }

        var links = PortalLinks.ForListedProduct(HostMap, Portal, product.Key, host);
        return links.Absolute(links.ProductHome(product.Key));
    }
}
