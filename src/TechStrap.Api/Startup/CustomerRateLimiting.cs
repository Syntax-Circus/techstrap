using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;

namespace TechStrap.Api.Startup;

public static class CustomerRateLimiting
{
    /// <summary>Adds the two customer-link policies, each per client IP. Call inside AddRateLimiter.</summary>
    public static RateLimiterOptions AddCustomerPolicies(this RateLimiterOptions options, CustomerRateLimitOptions limits)
    {
        options.AddPerIpFixedWindow(
            CustomerRateLimitOptions.TokenAccessPolicyName,
            limits.TokenAccessPermitLimit,
            TimeSpan.FromSeconds(limits.TokenAccessWindowSeconds));
        options.AddPerIpFixedWindow(
            CustomerRateLimitOptions.LostLinkPolicyName,
            limits.LostLinkPermitLimit,
            TimeSpan.FromSeconds(limits.LostLinkWindowSeconds));
        return options;
    }
}
