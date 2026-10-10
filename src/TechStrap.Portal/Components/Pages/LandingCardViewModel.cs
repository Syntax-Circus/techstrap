using TechStrap.Contracts.Skins;

namespace TechStrap.Portal.Components.Pages;

/// <summary>One card of the landing page (D-052): text the page encodes, a logo address that already passed the https rule, the href <c>PortalLinks</c> built, and the card's own resolved skin (the default pack with the product's overrides and accent, validated by the resolver). Presentation only.</summary>
internal sealed record LandingCardViewModel(string Key, string DisplayName, string? Tagline, string? LogoUrl, string Href, ResolvedSkin Skin);
