using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TechStrap.Api.Security;

/// <summary>
/// Placeholder authentication scheme for PHASE-01. The default-deny fallback policy needs a scheme to
/// challenge with; without one, a request that needs authentication throws instead of returning 401.
/// It never authenticates anyone. PHASE-04 replaces it with OIDC JWT bearer authentication.
/// </summary>
public static class UnauthenticatedScheme
{
    public const string Name = "Unauthenticated";
}

public sealed class UnauthenticatedSchemeHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
