namespace TechStrap.Infrastructure.Seeding;

/// <summary>
/// Fixed, obviously fake API keys for the development seed data, so local intake calls work. Development only; documented in
/// docs/development/DEV-DATA.md. They are never valid anywhere else because only development seeding stores their hashes.
/// </summary>
public static class DevelopmentApiKeys
{
    public const string OrbitlyTrusted = "tsk_devOrbitlyServerKeyNotASecret00000000000000";
    public const string OrbitlyPublic = "tsp_devOrbitlyAppKeyNotASecret00000000000000000";
    public const string PaperplaneTrusted = "tsk_devPaperplaneServerKeyNotASecret00000000000";
}
