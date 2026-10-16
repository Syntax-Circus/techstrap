using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Features;
using TechStrap.Portal.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

public partial class Features : ProductPageBase
{
    [SupplyParameterFromQuery(Name = PortalRoutes.QueryParameter)]
    private string? Query { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.StatusParameter)]
    private string? StatusQuery { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.PageParameter)]
    private string? PageText { get; set; }

    [Inject]
    private IPublicFeaturesClient Client { get; set; } = default!;

    private string Text { get; set; } = string.Empty;
    private string? Status { get; set; }
    private PagedResponse<PublicFeatureSummaryDto>? Results { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null) return;
        Text = Query?.Trim() ?? string.Empty;
        if (Text.Length > 200) Text = Text[..200];
        Status = FeatureCopy.Statuses.Contains(StatusQuery) ? StatusQuery : null;
        var page = KbPaging.Parse(PageText);
        var result = await Client.ListAsync(Key, Text, Status, page, 25, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }
        if (page > 1 && result.Value.Items.Count == 0)
        {
            NotFoundAfterTheming();
            return;
        }
        Results = result.Value;
    }
}
