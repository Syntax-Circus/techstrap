using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Features.Appearance;

/// <summary>A scoped example of the public portal using the shared, validated skin resolution.</summary>
public partial class PortalAppearancePreview
{
    /// <summary>The product overrides currently being edited.</summary>
    [Parameter]
    public ProductSkin? Skin { get; set; }

    /// <summary>The deployment's default pack.</summary>
    [Parameter]
    public string DefaultPack { get; set; } = SkinPacks.DefaultKey;

    /// <summary>The product accent used when no skin brand overrides it.</summary>
    [Parameter]
    public string? AccentColour { get; set; }

    /// <summary>The product display name, rendered as text.</summary>
    [Parameter]
    public string DisplayName { get; set; } = "Product help";

    private string? _style;
    private Dictionary<string, object> _attributes = [];
    private IReadOnlyList<SkinProblem> _problems = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var resolution = SkinResolver.Resolve(DefaultPack, Skin, AccentColour);
        var properties = SkinCss.Properties(resolution.Skin);
        _style = properties.Count == 0 ? null : string.Join(";", properties.Select(p => $"{p.Key}:{p.Value}"));
        _attributes = SkinCss.Attributes(resolution.Skin).ToDictionary(p => p.Key, p => (object)p.Value);
        _problems = resolution.Problems;
    }
}
