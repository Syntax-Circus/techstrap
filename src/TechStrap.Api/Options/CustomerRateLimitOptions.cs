namespace TechStrap.Api.Options;

/// <summary>Customer link route limits (D-038), per client IP. Validated at boot.</summary>
public sealed class CustomerRateLimitOptions
{
    public const string SectionName = "RateLimiting:Customer";
    public const string TokenAccessPolicyName = "token-access";
    public const string LostLinkPolicyName = "lost-link";

    public int TokenAccessPermitLimit { get; set; } = 60;

    public int TokenAccessWindowSeconds { get; set; } = 60;

    public int LostLinkPermitLimit { get; set; } = 5;

    public int LostLinkWindowSeconds { get; set; } = 3600;
}
