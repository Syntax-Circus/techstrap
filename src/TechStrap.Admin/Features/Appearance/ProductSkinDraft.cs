using System.Text.Json;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Features.Appearance;

/// <summary>Separate applied structured skin and unapplied advanced text. Null tokens stay inherited.</summary>
public sealed class ProductSkinDraft
{
    public ProductSkin? Skin { get; private set; }
    public string Json { get; private set; } = string.Empty;
    public string RawJson { get; private set; } = string.Empty;
    public bool HasUnappliedJson => RawJson != Json;
    public string? JsonError { get; private set; }
    public string? DefaultPack { get; set; }

    public void Load(string json)
    {
        Json = RawJson = json;
        SkinSerializer.TryDeserialize(json, out var skin);
        Skin = skin;
        JsonError = null;
    }

    public void EditJson(string json)
    {
        RawJson = json;
        JsonError = !SkinSerializer.TryDeserialize(json, out _) ? "Enter a valid skin JSON object with known fields." : null;
    }

    public bool ApplyJson()
    {
        if (!SkinSerializer.TryDeserialize(RawJson, out var skin))
        {
            JsonError = "Enter a valid skin JSON object with known fields.";
            return false;
        }
        Skin = skin;
        Json = RawJson;
        JsonError = null;
        return true;
    }

    public void DiscardJson() { RawJson = Json; JsonError = null; }

    public void Set(string field, string? value)
    {
        if (HasUnappliedJson) return;
        value = string.IsNullOrEmpty(value) ? null : value;
        var skin = Skin ?? new ProductSkin();
        Skin = field switch
        {
            "pack" => skin with { Pack = value },
            "background" => skin with { Background = value },
            "surface" => skin with { Surface = value },
            "ink" => skin with { Ink = value },
            "muted" => skin with { Muted = value },
            "border" => skin with { Border = value },
            "brand" => skin with { Brand = value },
            "chrome" => skin with { Chrome = value },
            "focus" => skin with { Focus = value },
            "headingFont" => skin with { HeadingFont = value },
            "bodyFont" => skin with { BodyFont = value },
            "radius" => skin with { Radius = value },
            "borderWidth" => skin with { BorderWidth = value is null ? null : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            "shadow" => skin with { Shadow = value },
            "button" => skin with { Button = value },
            "header" => skin with { Header = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Json = RawJson = Format(Skin);
    }

    public void Remove() { Skin = null; Json = RawJson = string.Empty; JsonError = null; }

    public string? Problem()
    {
        if (HasUnappliedJson) return JsonError ?? "Apply JSON or discard its changes before saving.";
        if (Skin is null || Skin.IsEmpty) return null;
        var invalid = SkinRules.Validate(Skin).FirstOrDefault();
        if (invalid is not null) return $"Choose a valid value for {invalid.Target}.";
        if (Skin.Pack is null && DefaultPack is null) return "The deployment default is unavailable. Retry before saving appearance changes; effective contrast is unknown.";
        var contrast = SkinResolver.Resolve(DefaultPack, Skin, null).Problems.FirstOrDefault();
        return contrast is null ? null : $"Appearance contrast is too low: {contrast.Target}.";
    }

    private static string Format(ProductSkin? skin)
    {
        var compact = SkinSerializer.Serialize(skin);
        if (compact is null) return string.Empty;
        using var document = JsonDocument.Parse(compact);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
