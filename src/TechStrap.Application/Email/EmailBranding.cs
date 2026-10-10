namespace TechStrap.Application.Email;

/// <summary>The branding a template needs, taken from the product at send time.</summary>
/// <param name="ChromeColour">The product skin's chrome colour for the header bar, or null for the accent bar. Validated again by the renderer.</param>
public sealed record EmailBranding(string DisplayName, string? LogoPath, string AccentColour, string? FromAddress, string? ReplyTo, string? ChromeColour = null);
