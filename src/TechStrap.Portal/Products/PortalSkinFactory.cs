using TechStrap.Contracts.Skins;

namespace TechStrap.Portal.Products;

/// <summary>
/// The one place a page turns the default pack, a product's skin and its accent into the skin it renders (D-053). It wraps <see cref="SkinResolver.Resolve"/>, which never throws and drops every invalid or
/// low-contrast override, so components never hand raw skin text to anything else. What was dropped is logged by token name only, never by value.
/// </summary>
public sealed class PortalSkinFactory(ILogger<PortalSkinFactory> logger)
{
    public ResolvedSkin Resolve(string defaultPack, ProductSkin? skin, string? accent)
    {
        var resolution = SkinResolver.Resolve(defaultPack, skin, accent);
        if (resolution.Problems.Count > 0 && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("A product skin override was ignored: {Tokens}.", string.Join(", ", resolution.Problems.Select(p => p.Target)));
        }

        return resolution.Skin;
    }
}
