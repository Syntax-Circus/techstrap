namespace TechStrap.Contracts.Settings;

/// <summary>The deployment-wide settings an Admin reads (D-053). <paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateSiteSettingsRequest"/>.</summary>
/// <param name="DefaultPack">The key of the theme pack products fall back to when they carry no skin.</param>
/// <param name="Version">The concurrency token.</param>
public sealed record SiteSettingsDto(string DefaultPack, uint Version);

/// <summary><paramref name="Version"/> must equal the version last read; otherwise the update is a 409 conflict.</summary>
/// <param name="DefaultPack">The key of a theme pack. Null leaves the stored pack unchanged.</param>
/// <param name="Version">The version last read.</param>
public sealed record UpdateSiteSettingsRequest(string? DefaultPack, uint Version);

/// <summary>What the Portal needs from the settings, anonymously (D-053): the default pack key and nothing else.</summary>
/// <param name="DefaultPack">The key of the theme pack products fall back to when they carry no skin.</param>
public sealed record PublicSiteDto(string DefaultPack);
