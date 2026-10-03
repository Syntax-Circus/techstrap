using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace TechStrap.Portal.Components.Ui;

/// <summary>The only TechStrap element a customer sees: a small "Powered by TechStrap" link (docs/BRAND.md section 8), hideable per installation.</summary>
public partial class PoweredByFooter
{
    private const string RepositoryUrl = "https://github.com/Syntax-Circus/techstrap";

    private bool _show;

    [Inject]
    private IOptions<PoweredByOptions> Options { get; set; } = default!;

    protected override void OnInitialized() => _show = Options.Value.Show;
}
