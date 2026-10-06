using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The Portal's root. With <c>TECHSTRAP_PORTAL_DEFAULT_PRODUCT</c> set it sends the visitor to that product's home (a 302; the API is not asked, and the product page decides whether it exists). Without
/// it the root is a neutral page with no list of products, so nothing can be enumerated.
/// </summary>
public partial class Home
{
    [Inject]
    private IOptions<PortalOptions> Portal { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (Portal.Value.DefaultProductKeyOrNull is { } key)
        {
            Navigation.NavigateTo(PortalRoutes.ProductHome(key));
        }
    }
}
