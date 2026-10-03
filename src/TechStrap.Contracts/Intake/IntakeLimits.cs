namespace TechStrap.Contracts.Intake;

/// <summary>Intake limits (D-032). The API enforces them; the Portal and SDK may check them before uploading.</summary>
public static class IntakeLimits
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const long MaxMessageBytes = 25L * 1024 * 1024;
    public const int MaxFiles = 5;
    public const int MaxMetadataKeys = 50;
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 1_000;
    public const int MaxIdempotencyKeyLength = 200;
    public static readonly IReadOnlyList<string> AllowedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".log", ".csv", ".zip"];
}

public static class IntakeWarnings
{
    public const string ExternalUserRefIgnored = "external-user-ref-ignored";
}
