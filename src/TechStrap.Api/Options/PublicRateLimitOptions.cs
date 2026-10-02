namespace TechStrap.Api.Options;

/// <summary>
/// Limits for anonymous public endpoints, bound from RateLimiting:Public and validated at startup
/// (a bad value fails the boot instead of 500ing on the first request).
/// </summary>
public sealed class PublicRateLimitOptions
{
    public const string SectionName = "RateLimiting:Public";

    /// <summary>Name of the rate-limit policy that public endpoints attach with RequireRateLimiting.</summary>
    public const string PolicyName = "public";

    public int PermitLimit { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;
}
