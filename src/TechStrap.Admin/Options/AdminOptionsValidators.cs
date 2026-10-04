using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Admin.Options;

/// <summary>Fails the start when the OIDC settings are missing or malformed. Messages name the environment variable so the operator knows what to set.</summary>
internal sealed class AdminAuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Auth:Authority (AUTH__AUTHORITY) must be the absolute URL of the OIDC provider, for example https://auth.example.com/application/o/techstrap-admin/.");
        }
        else if (authority.Scheme == Uri.UriSchemeHttp && !environment.IsDevelopment())
        {
            failures.Add("Auth:Authority (AUTH__AUTHORITY) must use https outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Auth:ClientId (AUTH__CLIENTID) is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Auth:ClientSecret (AUTH__CLIENTSECRET) is required: the Admin OIDC client is confidential.");
        }

        if (!options.Scopes.Contains("openid", StringComparer.Ordinal) || !options.Scopes.Contains("offline_access", StringComparer.Ordinal))
        {
            failures.Add("Auth:Scopes must include openid and offline_access: the API token is refreshed with the refresh token.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The API base address and timeout (the Blazor.Auth ApiOptions, section "Api", env API__BASEURL).</summary>
internal sealed class AdminApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public const int MaxTimeoutSeconds = 300;

    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl) || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Api:BaseUrl (API__BASEURL) must be the absolute URL of the TechStrap API, for example http://api/.");
        }

        if (options.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            failures.Add($"Api:TimeoutSeconds (API__TIMEOUTSECONDS) must be between 1 and {MaxTimeoutSeconds}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The same rules the API applies to its group keys (AgentAuthenticationSetup): none blank, and the two groups differ.</summary>
internal sealed class AgentGroupOptionsValidator : IValidateOptions<AgentGroupOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentGroupOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AgentGroup))
        {
            failures.Add($"{AgentGroupOptions.AgentGroupKey} must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminGroup))
        {
            failures.Add($"{AgentGroupOptions.AdminGroupKey} must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.GroupClaimType))
        {
            failures.Add($"{AgentGroupOptions.GroupClaimTypeKey} must not be blank.");
        }

        if (string.Equals(options.AgentGroup, options.AdminGroup, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AgentGroupOptions.AgentGroupKey} and {AgentGroupOptions.AdminGroupKey} must name different groups.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
