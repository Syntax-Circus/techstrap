using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Components.Pages;

namespace TechStrap.Admin.Auth;

/// <summary>
/// Cookie plus OpenID Connect sign-in (D-040). The cookie scheme is literally "Cookies": SyntaxCircus.Blazor.Auth reads and refreshes the saved
/// tokens from the scheme with that name. The sign-in, sign-in start and sign-out routes are minimal-API endpoints, not components, so they never need a circuit.
/// </summary>
public static class AdminAuthentication
{
    public const string CookieScheme = "Cookies";
    public const string OidcScheme = "oidc";

    public const string SignInPath = "/signin";
    public const string SignInStartPath = "/signin/start";
    public const string SignOutPath = "/signout";
    public const string OidcCallbackPath = "/signin-oidc";
    public const string OidcSignedOutCallbackPath = "/signout-callback-oidc";
    public const string ReturnUrlParameter = "returnUrl";
    public const string FailedParameter = "failed";

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "techstrap.admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;
                // An unauthenticated request is sent to the signed-out landing page, never straight to the provider.
                options.LoginPath = SignInPath;
                options.ReturnUrlParameter = ReturnUrlParameter;
            })
            .AddOpenIdConnect(OidcScheme, _ => { });

        // Secure cookies everywhere except local development over plain http.
        services.AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<IHostEnvironment>((options, environment) =>
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

        // Read lazily from the validated options, so test factories can supply the values through in-memory configuration.
        services.AddOptions<OpenIdConnectOptions>(OidcScheme)
            .Configure<IOptions<AuthOptions>, IHostEnvironment>((options, auth, environment) =>
            {
                var settings = auth.Value;
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SignInScheme = CookieScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                // Raw claim names (sub, email, name, groups), the same as the API sees.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.CallbackPath = OidcCallbackPath;
                options.SignedOutCallbackPath = OidcSignedOutCallbackPath;
                // Front-channel logout from the provider is not used in 07a. The default /signout-oidc answers a plain GET by signing the
                // user out, so any cross-site link could log an agent out (the cookie is SameSite=Lax). An empty path disables the endpoint.
                options.RemoteSignOutPath = PathString.Empty;
                options.Scope.Clear();
                foreach (var scope in settings.Scopes)
                {
                    options.Scope.Add(scope);
                }

                // Never show the provider's failure text (it can carry request details): send the agent back to the landing page.
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect($"{SignInPath}?{FailedParameter}=1");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SignInPath, (string? returnUrl, string? failed) =>
                new RazorComponentResult<SignInLanding>(new { ReturnUrl = LocalReturnUrl.Sanitize(returnUrl), Failed = !string.IsNullOrEmpty(failed) }))
            .AllowAnonymous();

        endpoints.MapGet(SignInStartPath, (string? returnUrl) =>
                TypedResults.Challenge(new AuthenticationProperties { RedirectUri = LocalReturnUrl.Sanitize(returnUrl) }, [OidcScheme]))
            .AllowAnonymous();

        // POST only. Binding the form makes minimal APIs reject a request without a valid antiforgery token (400); signs out of the cookie and of the provider.
        endpoints.MapPost(SignOutPath, async (IFormCollection form, HttpContext context, IServerTokenCache tokens, IUserTokenCacheKeyProvider keys) =>
        {
            // The server token cache is keyed per user and outlives the cookie: drop this user's tokens so a signed-out session leaves none behind.
            if (keys.GetCacheKey(context.User) is { } key)
            {
                await tokens.RemoveAsync(key, context.RequestAborted);
            }

            return TypedResults.SignOut(new AuthenticationProperties { RedirectUri = SignInPath }, [CookieScheme, OidcScheme]);
        });
        return endpoints;
    }
}
