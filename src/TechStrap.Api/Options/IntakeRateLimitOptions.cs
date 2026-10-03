namespace TechStrap.Api.Options;

/// <summary>Intake limits (02-ARCHITECTURE 11.2, D-001). Validated at boot.</summary>
public sealed class IntakeRateLimitOptions
{
    public const string SectionName = "RateLimiting:Intake";
    public const string WebFormPolicyName = "public-submit";
    public const string KeyPolicyName = "intake-key";

    /// <summary>Web form submissions per client IP.</summary>
    public int WebFormPermitLimit { get; set; } = 5;

    public int WebFormWindowSeconds { get; set; } = 600;

    /// <summary>Public-key submissions per key prefix and client IP.</summary>
    public int PublicKeyPermitLimit { get; set; } = 10;

    public int PublicKeyWindowSeconds { get; set; } = 60;

    /// <summary>Trusted-key submissions per key prefix.</summary>
    public int TrustedKeyPermitLimit { get; set; } = 120;

    public int TrustedKeyWindowSeconds { get; set; } = 60;
}
