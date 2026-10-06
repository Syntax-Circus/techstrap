using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The help-centre home of a product (P09-T12): the categories it can see, each with its published article count and description, through <see cref="IPublicKbClient"/>. The product loads first (the base class): an unknown,
/// inactive or malformed product is the neutral 404 before any KB call is made. A product with no category shows the empty state, not an error. Every name and description is plain text and encoded.
/// </summary>
public partial class KbHome : ProductPageBase
{
    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    private IReadOnlyList<PublicKbCategoryDto>? Categories { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        var result = await Kb.ListCategoriesAsync(Key, RequestAborted);
        if (result.IsSuccess)
        {
            Categories = result.Value;
            return;
        }

        Fail(result.Errors[0]);
    }

    private static IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme) =>
        [new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading)];
}
