using System.Text.Json.Serialization;

namespace TechStrap.Contracts.Skins;

/// <summary>
/// A product's skin: every field optional, null meaning inherit from the pack. Colors are <c>#RRGGBB</c>, fonts are <see cref="SkinFonts"/> keys,
/// the presets are the <see cref="SkinValues"/> constants. The server validates it against the same grammar (<see cref="SkinRules"/>) on save and on render.
/// </summary>
/// <param name="Pack">A key from <see cref="SkinPacks"/>.</param>
/// <param name="Background">Page background.</param>
/// <param name="Surface">Cards and panels.</param>
/// <param name="Ink">Body text.</param>
/// <param name="Muted">Secondary text.</param>
/// <param name="Border">Rules and borders.</param>
/// <param name="Brand">Brand color; when unset the product's accent color applies.</param>
/// <param name="Chrome">Header and footer fill.</param>
/// <param name="Focus">Focus ring color.</param>
/// <param name="HeadingFont">A <see cref="SkinFonts"/> key for headings.</param>
/// <param name="BodyFont">A <see cref="SkinFonts"/> key for body text.</param>
/// <param name="Radius">One of <see cref="SkinValues.RadiusSquare"/>, <see cref="SkinValues.RadiusSoft"/>, <see cref="SkinValues.RadiusRound"/>.</param>
/// <param name="BorderWidth">Border width in pixels, 1 to 4.</param>
/// <param name="Shadow">One of <see cref="SkinValues.ShadowNone"/>, <see cref="SkinValues.ShadowSoft"/>, <see cref="SkinValues.ShadowHard"/>.</param>
/// <param name="Button">One of <see cref="SkinValues.ButtonFlat"/>, <see cref="SkinValues.ButtonBevel"/>, <see cref="SkinValues.ButtonOutline"/>.</param>
/// <param name="Header">One of <see cref="SkinValues.HeaderPlain"/>, <see cref="SkinValues.HeaderSolid"/>, <see cref="SkinValues.HeaderBand"/>.</param>
public sealed record ProductSkin(
    string? Pack = null,
    string? Background = null,
    string? Surface = null,
    string? Ink = null,
    string? Muted = null,
    string? Border = null,
    string? Brand = null,
    string? Chrome = null,
    string? Focus = null,
    string? HeadingFont = null,
    string? BodyFont = null,
    string? Radius = null,
    int? BorderWidth = null,
    string? Shadow = null,
    string? Button = null,
    string? Header = null)
{
    /// <summary>True when every field is null, meaning the product sets no skin.</summary>
    [JsonIgnore]
    public bool IsEmpty => this == new ProductSkin();
}
