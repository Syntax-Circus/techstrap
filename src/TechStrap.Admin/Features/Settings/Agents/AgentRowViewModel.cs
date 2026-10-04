using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>One row of the agents page. An Admin gets every field; a field the API left out (null) is shown as a dash and offers no action.</summary>
internal sealed record AgentRowViewModel(Guid Id, string DisplayName, string? Email, string? Role, bool? IsActive, DateTimeOffset? LastSeenAt)
{
    public static AgentRowViewModel From(AgentListItemDto agent) =>
        new(agent.Id, string.IsNullOrWhiteSpace(agent.Name) ? agent.DisplayLabel : agent.Name, agent.Email, agent.Role, agent.IsActive, agent.LastSeenAt);

    /// <summary>The row after the API answered a change: its fresh role, active flag and last-seen time, with the name the list already showed.</summary>
    public AgentRowViewModel With(AgentDto agent) => this with { Email = agent.Email, Role = agent.Role, IsActive = agent.IsActive, LastSeenAt = agent.LastSeenAt };
}
