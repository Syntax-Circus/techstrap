using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid RaeAgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A row of the Admin agent list: every field is filled in, as the API does for an Admin.</summary>
    public static AgentListItemDto AgentRow(string name, Guid id, string role = AgentRoles.Agent, bool active = true, DateTimeOffset? lastSeen = null, string? email = null) =>
        new(id, name, name, email ?? $"{name.Split(' ')[0].ToLowerInvariant()}@example.com", role, active, lastSeen);

    /// <summary>The agent's own record, as <c>GET /api/agents/me</c> answers it.</summary>
    public static AgentDto Me(bool admin, string? publicName = null, bool active = true) => new(
        admin ? AdaAgentId : SamAgentId, admin ? "Ada Admin" : "Sam Ortiz", admin ? "ada@example.com" : "sam@example.com",
        admin ? AgentRoles.Admin : AgentRoles.Agent, active, publicName, null);

    public static NotificationPreferenceDto Pref(string product, bool on = false, Guid? id = null) => new(id ?? Guid.NewGuid(), product, on);
}
