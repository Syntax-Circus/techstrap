namespace TechStrap.Admin.Options;

/// <summary>
/// The agent and admin group names and the group claim type, under the same flat keys and defaults the API reads (AgentAccessOptions), so one
/// env file configures both hosts. Admin never parses group claims: access comes from <c>GET /api/agents/me</c> (D-040). The names exist
/// so the no-access page can say which group to ask for, and so a mismatch between the two hosts fails at start rather than in production.
/// </summary>
public sealed class AgentGroupOptions
{
    public const string AgentGroupKey = "TECHSTRAP_AGENT_GROUP";
    public const string AdminGroupKey = "TECHSTRAP_ADMIN_GROUP";
    public const string GroupClaimTypeKey = "TECHSTRAP_GROUP_CLAIM_TYPE";

    public const string DefaultAgentGroup = "techstrap-agents";
    public const string DefaultAdminGroup = "techstrap-admins";
    public const string DefaultGroupClaimType = "groups";

    public string AgentGroup { get; set; } = DefaultAgentGroup;

    public string AdminGroup { get; set; } = DefaultAdminGroup;

    public string GroupClaimType { get; set; } = DefaultGroupClaimType;
}
