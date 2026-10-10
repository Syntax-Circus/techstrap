namespace TechStrap.Contracts.Skins;

/// <summary>A complete set of skin tokens: what a pack holds and what a resolved skin carries. Colours are uppercase <c>#RRGGBB</c>.</summary>
/// <param name="Background">Page background.</param>
/// <param name="Surface">Cards and panels.</param>
/// <param name="Ink">Body text.</param>
/// <param name="Muted">Secondary text.</param>
/// <param name="Border">Rules and borders.</param>
/// <param name="Brand">Brand colour.</param>
/// <param name="Chrome">Header and footer fill.</param>
/// <param name="Focus">Focus ring colour.</param>
/// <param name="HeadingFont">A <see cref="SkinFonts"/> key.</param>
/// <param name="BodyFont">A <see cref="SkinFonts"/> key.</param>
/// <param name="Radius">A radius preset from <see cref="SkinValues"/>.</param>
/// <param name="BorderWidth">Border width in pixels, 1 to 4.</param>
/// <param name="Shadow">A shadow preset from <see cref="SkinValues"/>.</param>
/// <param name="Button">A button preset from <see cref="SkinValues"/>.</param>
/// <param name="Header">A header preset from <see cref="SkinValues"/>.</param>
public sealed record SkinTokens(
    string Background,
    string Surface,
    string Ink,
    string Muted,
    string Border,
    string Brand,
    string Chrome,
    string Focus,
    string HeadingFont,
    string BodyFont,
    string Radius,
    int BorderWidth,
    string Shadow,
    string Button,
    string Header);

/// <summary>A named, vetted token set with a declared scheme.</summary>
/// <param name="Key">The stable key stored in settings and skins.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scheme"><see cref="SkinValues.Light"/> or <see cref="SkinValues.Dark"/>.</param>
/// <param name="Tokens">The complete token set.</param>
public sealed record SkinPack(string Key, string Name, string Scheme, SkinTokens Tokens);
