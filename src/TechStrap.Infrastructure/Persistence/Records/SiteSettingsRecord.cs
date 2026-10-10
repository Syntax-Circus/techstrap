namespace TechStrap.Infrastructure.Persistence.Records;

/// <summary>The one row of <c>site_settings</c> (D-053). Persistence shape only (D-026); the domain type is <c>SiteSettings</c>.</summary>
internal sealed class SiteSettingsRecord
{
    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public short Id { get; set; }

    public string DefaultPack { get; set; } = string.Empty;

    /// <summary>Postgres <c>xmin</c>, the optimistic concurrency token.</summary>
    public uint Version { get; set; }

    public const short SingletonId = 1;
}
