using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TechStrap.Api.Tests.Auth;

/// <summary>
/// A locally signed test issuer. Tests never contact a real IdP: the JWT bearer handler gets a static OIDC configuration
/// holding this HS256 key (the pattern from cmsify's OidcAuthenticationApiTests).
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://issuer.techstrap.test/";
    public const string Audience = "techstrap-api";
    public const string AgentGroup = "techstrap-agents";
    public const string AdminGroup = "techstrap-admins";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("techstrap-test-signing-key-not-a-secret-0123456789"));

    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Authentication:JwtBearer:Authority"] = Issuer,
        ["Authentication:JwtBearer:Audiences:0"] = Audience,
    };

    public static void Configure(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                IssuerSigningKey = SigningKey,
                ClockSkew = TimeSpan.Zero,
            };
        });

    public static string Token(
        string subject,
        IEnumerable<string> groups,
        string? email = "agent@example.com",
        string? name = "Test Agent",
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        claims.AddRange(groups.Select(group => new Claim("groups", group)));
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now.AddMinutes(-5),
            Expires = expires ?? now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpClient Bearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
