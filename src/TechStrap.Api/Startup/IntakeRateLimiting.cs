using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Application.ApiKeys;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Startup;

public static class IntakeRateLimiting
{
    /// <summary>Adds the two intake policies. Call inside AddRateLimiter, next to the existing "public" policy.</summary>
    public static RateLimiterOptions AddIntakePolicies(this RateLimiterOptions options, IntakeRateLimitOptions limits)
    {
        options.AddPerIpFixedWindow(
            IntakeRateLimitOptions.WebFormPolicyName,
            limits.WebFormPermitLimit,
            TimeSpan.FromSeconds(limits.WebFormWindowSeconds));

        options.AddPolicy(IntakeRateLimitOptions.KeyPolicyName, context =>
        {
            var (key, trusted) = KeyPartition(context);
            var (limit, window) = trusted
                ? (limits.TrustedKeyPermitLimit, limits.TrustedKeyWindowSeconds)
                : (limits.PublicKeyPermitLimit, limits.PublicKeyWindowSeconds);
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(window),
                QueueLimit = 0,
            });
        });
        return options;
    }

    /// <summary>The partition for a request on the key policy. Internal for tests.</summary>
    internal static (string Key, bool Trusted) KeyPartition(HttpContext context)
    {
        // The limiter runs before authentication, so read the raw header.
        var raw = context.Request.Headers[HeaderNames.ApiKey].FirstOrDefault()?.Trim();
        var prefix = string.IsNullOrEmpty(raw)
            ? null
            : raw[..Math.Min(raw.Length, ApiKeyFormat.StoredPrefixLength)];
        if (prefix is not null && raw!.StartsWith(ApiKeyFormat.TrustedPrefix, StringComparison.Ordinal))
        {
            return ($"trusted:{prefix}", true);
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return ($"key:{prefix ?? "none"}:{ip}", false);
    }
}
