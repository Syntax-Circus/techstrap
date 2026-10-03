namespace TechStrap.Api.Options;

/// <summary>
/// Which IdP groups grant agent and admin access, and which claim carries them (D-004, D-029). The settings are flat
/// environment names (no section), so they are bound by key in AgentAuthenticationSetup.
/// </summary>
public sealed class AgentAccessOptions
{
    public const string AgentGroupKey = "TECHSTRAP_AGENT_GROUP";
    public const string AdminGroupKey = "TECHSTRAP_ADMIN_GROUP";
    public const string GroupClaimTypeKey = "TECHSTRAP_GROUP_CLAIM_TYPE";

    public string AgentGroup { get; set; } = "techstrap-agents";

    public string AdminGroup { get; set; } = "techstrap-admins";

    public string GroupClaimType { get; set; } = "groups";
}
