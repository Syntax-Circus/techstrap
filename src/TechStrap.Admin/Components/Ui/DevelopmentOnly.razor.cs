using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Renders its content in the Development environment only. Anywhere else it renders nothing and reports the page as not
/// found, so the style guide cannot be reached, or told apart from any other unknown address, in production.
/// </summary>
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
