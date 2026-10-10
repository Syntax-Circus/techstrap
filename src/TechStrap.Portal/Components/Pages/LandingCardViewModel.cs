namespace TechStrap.Portal.Components.Pages;

/// <summary>One card of the landing page (D-052): text the page encodes, a logo address that already passed the https rule, and the href <c>PortalLinks</c> built. Presentation only.</summary>
internal sealed record LandingCardViewModel(string Key, string DisplayName, string? Tagline, string? LogoUrl, string Href);
