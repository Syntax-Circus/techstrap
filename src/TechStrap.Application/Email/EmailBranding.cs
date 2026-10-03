namespace TechStrap.Application.Email;

/// <summary>The branding a template needs, taken from the product at send time.</summary>
public sealed record EmailBranding(string DisplayName, string? LogoPath, string AccentColour, string? FromAddress, string? ReplyTo);
