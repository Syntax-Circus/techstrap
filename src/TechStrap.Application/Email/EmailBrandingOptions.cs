namespace TechStrap.Application.Email;

/// <summary>Installation-wide email branding (D-024): <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY</c>, default true.</summary>
public sealed class EmailBrandingOptions
{
    public const string ShowPoweredByKey = "TECHSTRAP_PORTAL_SHOW_POWERED_BY";
    public const string PoweredByUrl = "https://github.com/Syntax-Circus/techstrap";
    public bool ShowPoweredBy { get; set; } = true;
}
