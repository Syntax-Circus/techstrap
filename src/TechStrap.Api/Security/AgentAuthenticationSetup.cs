using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Authentication;
using TechStrap.Api.Options;
using TechStrap.Application.Agents;

namespace TechStrap.Api.Security;

/// <summary>Agent sign-in: OIDC JWT bearer plus the Agent and Admin group policies (D-004, D-029).</summary>
public static class AgentAuthenticationSetup
{
    public const string JwtSection = "Authentication:JwtBearer";

    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSyntaxCircusJwtBearer(configuration);

        // Keep raw OIDC claim names (sub, email, name, groups) so ClaimsCurrentAgentClaims and TECHSTRAP_GROUP_CLAIM_TYPE
        // mean exactly what the IdP sends.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.MapInboundClaims = false);

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSection))
            .Validate(
                settings => environment.IsDevelopment() || (!string.IsNullOrWhiteSpace(settings.Authority) && settings.Audiences.Any(audience => !string.IsNullOrWhiteSpace(audience))),
                "Authentication:JwtBearer:Authority and Audiences:0 are required outside Development.")
            .ValidateOnStart();

        services.AddOptions<AgentAccessOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                options.AgentGroup = config[AgentAccessOptions.AgentGroupKey] ?? options.AgentGroup;
                options.AdminGroup = config[AgentAccessOptions.AdminGroupKey] ?? options.AdminGroup;
                options.GroupClaimType = config[AgentAccessOptions.GroupClaimTypeKey] ?? options.GroupClaimType;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.AgentGroup), $"{AgentAccessOptions.AgentGroupKey} must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AdminGroup), $"{AgentAccessOptions.AdminGroupKey} must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.GroupClaimType), $"{AgentAccessOptions.GroupClaimTypeKey} must not be blank.")
            .Validate(
                options => !string.Equals(options.AgentGroup.Trim(), options.AdminGroup.Trim(), StringComparison.OrdinalIgnoreCase),
                $"{AgentAccessOptions.AgentGroupKey} and {AgentAccessOptions.AdminGroupKey} must be different groups.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAgentClaims, ClaimsCurrentAgentClaims>();
        services.AddScoped<IAuthorizationHandler, AgentAccessAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Agent, policy => policy.RequireAuthenticatedUser().AddRequirements(new AgentAccessRequirement(adminOnly: false)))
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireAuthenticatedUser().AddRequirements(new AgentAccessRequirement(adminOnly: true)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>The subset of the package's JWT settings that start-up validation checks.</summary>
    public sealed class JwtSettings
    {
        public string Authority { get; set; } = string.Empty;

        public List<string> Audiences { get; set; } = [];
    }
}
