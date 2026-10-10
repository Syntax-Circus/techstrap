using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Settings;

/// <summary>Deployment-wide settings (D-053): currently the default Portal theme pack. A single row; whether a key names a known pack is the Application handler's check.</summary>
public sealed class SiteSettings
{
    /// <summary>The pack key seeded for a new deployment.</summary>
    public const string DefaultPack = "classic";

    private SiteSettings(string defaultPackKey, uint version)
    {
        DefaultPackKey = defaultPackKey;
        Version = version;
    }

    /// <summary>The key of the theme pack products fall back to when they carry no skin.</summary>
    public string DefaultPackKey { get; private set; }

    /// <summary>Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>).</summary>
    public uint Version { get; }

    public static SiteSettings Restore(string defaultPackKey, uint version) => new(defaultPackKey, version);

    /// <summary>Sets the default pack key (trimmed and lower-cased); blank or over-long keys are refused.</summary>
    public DomainResult SetDefaultPack(string? key)
    {
        var value = key?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > DomainLimits.PackKeyMaxLength)
        {
            return DomainErrors.Validation("skin-pack-invalid", "Choose one of the available theme packs.", "default-pack");
        }

        DefaultPackKey = value;
        return DomainResult.Ok();
    }
}
