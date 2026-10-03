namespace TechStrap.Api.Security;

/// <summary>Policy names (02-ARCHITECTURE section 4, D-022, D-029).</summary>
public static class AuthorizationPolicies
{
    /// <summary>Agent group or admin group, and not deactivated.</summary>
    public const string Agent = "Agent";

    /// <summary>Admin group, and not deactivated.</summary>
    public const string Admin = "Admin";

    /// <summary>Product API key only (the ApiKey scheme); requires the product claim.</summary>
    public const string ApiKey = "ApiKey";

    /// <summary>Admits everyone; marks a route as deliberately anonymous (D-034).</summary>
    public const string Public = "Public";
}
