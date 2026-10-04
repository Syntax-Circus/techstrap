using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>
/// A signed-in test agent. Admin host tests never touch OpenID Connect: a request carries the header <see cref="AdminTestAuth.PrincipalHeader"/>
/// with one of the names below and the "Test" scheme turns it into a principal with the claim names the real sign-in produces
/// (sub, name, email, groups). No header means an anonymous request.
/// </summary>
public sealed record AdminTestPrincipal(string Name, string Subject, string DisplayName, string Email, string[] Groups)
{
    /// <summary>A member of the agent group.</summary>
    public static AdminTestPrincipal Agent { get; } = new("agent", "agent-sub-1", "Sam Agent", "sam@orbitly.test", ["techstrap-agents"]);

    /// <summary>A member of the admin group.</summary>
    public static AdminTestPrincipal Admin { get; } = new("admin", "admin-sub-1", "Ada Admin", "ada@orbitly.test", ["techstrap-admins"]);

    /// <summary>Signed in with the provider but in neither group: the API answers 403 agent-access-required.</summary>
    public static AdminTestPrincipal Outsider { get; } = new("outsider", "outsider-sub-1", "Olly Outsider", "olly@orbitly.test", []);

    public static IReadOnlyList<AdminTestPrincipal> All { get; } = [Agent, Admin, Outsider];

    /// <summary>The bearer token the Admin sends to the API for this principal. It is unique per principal so the stub API can tell them apart.</summary>
    public string AccessToken => AdminTestAuth.AccessTokenPrefix + Name;

    public static AdminTestPrincipal? Find(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    internal ClaimsPrincipal ToClaimsPrincipal(string scheme)
    {
        List<Claim> claims = [new("sub", Subject), new("name", DisplayName), new("email", Email), .. Groups.Select(g => new Claim("groups", g))];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, "name", "roles"));
    }
}

public static class AdminTestAuth
{
    public const string Scheme = "Test";
    public const string PrincipalHeader = "X-Test-Principal";

    /// <summary>The tokens look like secrets on purpose: the host tests scan the log for them (Review Focus 2).</summary>
    public const string AccessTokenPrefix = "test-access-token-";

    /// <summary>
    /// Adds the "Test" scheme as the default authenticate scheme. The real cookie scheme stays the default challenge scheme, so an anonymous
    /// request is still redirected to /signin. Each principal's access token is put in the server token cache, which is where
    /// SyntaxCircus.Blazor.Auth looks when the cookie holds no tokens, so the API-bound request carries "Bearer {AccessToken}".
    /// </summary>
    public static IServiceCollection AddAdminTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AdminTestAuthHandler>(Scheme, _ => { });
        services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = Scheme);
        // The provider metadata is fixed, so a challenge builds its redirect without any network call.
        services.Configure<OpenIdConnectOptions>("oidc", options => options.Configuration = new OpenIdConnectConfiguration
        {
            Issuer = "https://idp.test/application/o/techstrap-admin/",
            AuthorizationEndpoint = "https://idp.test/application/o/authorize/",
            TokenEndpoint = "https://idp.test/application/o/token/",
            EndSessionEndpoint = "https://idp.test/application/o/techstrap-admin/end-session/",
        });
        services.AddHostedService<AdminTestTokenSeeder>();
        return services;
    }

    /// <summary>The header a test client sends to sign in as <paramref name="principal"/>.</summary>
    public static HttpClient SignedInAs(this HttpClient client, AdminTestPrincipal principal)
    {
        client.DefaultRequestHeaders.Remove(PrincipalHeader);
        client.DefaultRequestHeaders.Add(PrincipalHeader, principal.Name);
        return client;
    }
}

internal sealed class AdminTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AdminTestAuth.PrincipalHeader, out var name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = AdminTestPrincipal.Find(name.ToString());
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail("Unknown test principal.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal.ToClaimsPrincipal(Scheme.Name), Scheme.Name)));
    }
}

/// <summary>Puts each test principal's access token in the server token cache under the key SyntaxCircus.Blazor.Auth uses ("user:{sub}").</summary>
internal sealed class AdminTestTokenSeeder(IServerTokenCache cache, IUserTokenCacheKeyProvider keys) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var principal in AdminTestPrincipal.All)
        {
            var key = keys.GetCacheKey(principal.Subject)!;
            await cache.SetAsync(key, new ServerTokenCacheEntry(principal.AccessToken, null, null, DateTimeOffset.UtcNow.AddDays(1)), cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
