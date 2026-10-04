using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Admin.Options;

public static class AdminOptionsRegistration
{
    /// <summary>The configuration section the OIDC settings live in. The existing env keys are AUTH__AUTHORITY, AUTH__CLIENTID and AUTH__CLIENTSECRET.</summary>
    public const string AuthSection = "Auth";

    /// <summary>
    /// Registers and validates the Admin options. Nothing is read here: every option is resolved lazily and <c>ValidateOnStart</c> checks it when the
    /// host starts, so a test factory can supply the settings through in-memory configuration. <c>AddBlazorTokenForwarding</c> binds
    /// <see cref="AuthOptions"/> (section <see cref="AuthSection"/>) and <see cref="ApiOptions"/> (section "Api"); this only adds the rules.
    /// </summary>
    public static IServiceCollection AddAdminOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AdminAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<ApiOptions>, AdminApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<AgentGroupOptions>, AgentGroupOptionsValidator>();

        services.AddOptions<AuthOptions>().ValidateOnStart();
        services.AddOptions<ApiOptions>().ValidateOnStart();
        services.AddOptions<AgentGroupOptions>()
            .Configure(options =>
            {
                options.AgentGroup = Read(configuration, AgentGroupOptions.AgentGroupKey, AgentGroupOptions.DefaultAgentGroup);
                options.AdminGroup = Read(configuration, AgentGroupOptions.AdminGroupKey, AgentGroupOptions.DefaultAdminGroup);
                options.GroupClaimType = Read(configuration, AgentGroupOptions.GroupClaimTypeKey, AgentGroupOptions.DefaultGroupClaimType);
            })
            .ValidateOnStart();
        return services;
    }

    // A key that is present but blank stays blank (and fails validation), exactly like the API; only an absent key takes the default.
    private static string Read(IConfiguration configuration, string key, string fallback) => configuration[key]?.Trim() ?? fallback;
}
