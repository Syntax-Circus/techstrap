namespace TechStrap.Contracts.Intake;

/// <summary>Intake limits (D-032). The API enforces them; the Portal and SDK may check them before uploading.</summary>
public static class IntakeLimits
{
    /// <summary>The largest single attachment, 10 MiB. The API rejects a larger file.</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;
    /// <summary>The largest total size of all attachments on one ticket or reply, 25 MiB. The API rejects a larger set.</summary>
    public const long MaxMessageBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The largest multipart request body of a ticket or a reply: 25 MiB of files plus 1 MiB for the text fields and the multipart framing. The API and the Portal both refuse a larger body before reading it;
    /// the handler still enforces the exact file limits.
    /// </summary>
    public const long FormBodyBytes = MaxMessageBytes + (1024 * 1024);

    /// <summary>The most attachments one ticket or reply may carry (5).</summary>
    public const int MaxFiles = 5;

    // The text limits of the contact and reply forms (D-045 addendum, 2026-10-06). The Domain owns the values (DomainLimits); IntakeLimitsParityTests keeps these copies equal.
    /// <summary>The longest requester name, in characters (100).</summary>
    public const int NameMaxLength = 100;
    /// <summary>The longest requester email address, in characters (320).</summary>
    public const int EmailMaxLength = 320;
    /// <summary>The longest ticket subject, in characters (200).</summary>
    public const int SubjectMaxLength = 200;
    /// <summary>The longest ticket or reply body, in characters (100,000).</summary>
    public const int BodyMaxLength = 100_000;
    /// <summary>The most metadata entries one ticket may carry (50).</summary>
    public const int MaxMetadataKeys = 50;
    /// <summary>The longest metadata key, in characters (64).</summary>
    public const int MaxMetadataKeyLength = 64;
    /// <summary>The longest metadata value, in characters (1,000).</summary>
    public const int MaxMetadataValueLength = 1_000;

    // The Domain owns the value (DomainLimits.MetadataMaxLength); IntakeLimitsParityTests keeps this copy equal.
    /// <summary>The longest metadata once serialized to JSON, in characters (16,000). The API rejects a larger set.</summary>
    public const int MaxMetadataJsonLength = 16_000;
    /// <summary>The longest <c>Idempotency-Key</c> the API accepts, in characters (200).</summary>
    public const int MaxIdempotencyKeyLength = 200;
    /// <summary>The attachment file extensions the API accepts, lower case with the leading dot.</summary>
    public static readonly IReadOnlyList<string> AllowedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".log", ".csv", ".zip"];
}

/// <summary>Machine-readable warnings the intake API returns with a ticket it accepted.</summary>
public static class IntakeWarnings
{
    /// <summary>The request carried an external user reference, which a public key may not set, so the API ignored it and created the ticket without it. Wire value <c>external-user-ref-ignored</c>.</summary>
    public const string ExternalUserRefIgnored = "external-user-ref-ignored";
}
