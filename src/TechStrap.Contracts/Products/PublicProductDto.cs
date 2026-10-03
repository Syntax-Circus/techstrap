namespace TechStrap.Contracts.Products;

/// <summary>Public branding for the portal. It never carries emails, keys or internal ids.</summary>
public sealed record PublicProductDto(string Key, string DisplayName, string? LogoPath, string AccentColour, string OnAccentColour, string AccentInkColour);
