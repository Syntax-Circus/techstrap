namespace TechStrap.Contracts.Intake;

/// <summary>Intake limits (D-032). The API enforces them; the Portal and SDK may check them before uploading.</summary>
public static class IntakeLimits
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const long MaxMessageBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The largest multipart request body of a ticket or a reply: 25 MiB of files plus 1 MiB for the text fields and the multipart framing. The API and the Portal both refuse a larger body before reading it;
    /// the handler still enforces the exact file limits.
    /// </summary>
    public const long FormBodyBytes = MaxMessageBytes + (1024 * 1024);

    public const int MaxFiles = 5;

    // The text limits of the contact and reply forms (D-045 addendum, 2026-10-06). The Domain owns the values (DomainLimits); IntakeLimitsParityTests keeps these copies equal.
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 320;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 100_000;
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
