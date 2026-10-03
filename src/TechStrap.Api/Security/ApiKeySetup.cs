using SyntaxCircus.AspNetCore.Authentication;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Security;

/// <summary>Product API-key sign-in on the SyntaxCircus API-key scheme, and the ApiKey and Public policies (D-034).</summary>
public static class ApiKeySetup
{
    public const string SchemeName = "ApiKey";

    public static IServiceCollection AddProductApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Keyed by scheme: the package's handler resolves the keyed validator from the request scope, so a scoped,
        // repository-backed validator works. The package's unkeyed constant validator stays registered but unused.
        services.AddKeyedScoped<IApiKeyValidator, ProductApiKeyValidator>(SchemeName);
        services.AddSyntaxCircusApiKey(configuration, schemeName: SchemeName);
        services.PostConfigure<ApiKeyAuthenticationOptions>(SchemeName, options => options.HeaderName = HeaderNames.ApiKey);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.ApiKey, policy => policy
                .AddAuthenticationSchemes(SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(ApiKeyClaimTypes.ProductId))
            .AddPolicy(AuthorizationPolicies.Public, policy => policy.RequireAssertion(_ => true));
        return services;
    }
}
