using Microsoft.AspNetCore.Components;

namespace TechStrap.Portal.Components.Ui;

/// <summary>Renders its content in Development only; anywhere else it renders nothing and reports the page as not found.</summary>
public partial class DevelopmentOnly
{
    private bool _isDevelopment;

    [Inject]
    private IHostEnvironment HostEnvironment { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        _isDevelopment = HostEnvironment.IsDevelopment();
        if (!_isDevelopment)
        {
            Navigation.NotFound();
        }
    }
}
