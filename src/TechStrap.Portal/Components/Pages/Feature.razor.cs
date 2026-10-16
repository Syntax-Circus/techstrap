using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Features;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Components.Pages;

public partial class Feature : ProductPageBase
{
    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Inject]
    private IPublicFeaturesClient Client { get; set; } = default!;

    private PublicFeatureDto? Entry { get; set; }
    private string Description { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null) return;
        var result = await Client.GetAsync(Key, Slug, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }
        Entry = result.Value;
        Description = KbPlainText.Describe(null, Entry.DescriptionHtml);
    }
}
