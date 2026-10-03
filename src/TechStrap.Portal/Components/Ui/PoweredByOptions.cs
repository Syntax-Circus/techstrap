namespace TechStrap.Portal.Components.Ui;

/// <summary>
/// Installation-wide setting for the "Powered by TechStrap" mark (D-024). Shown by default; the environment variable
/// <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY=false</c> hides it on every portal page. It is not per product.
/// </summary>
public sealed class PoweredByOptions
{
    public const string ConfigurationKey = "TECHSTRAP_PORTAL_SHOW_POWERED_BY";

    public bool Show { get; set; } = true;
}
