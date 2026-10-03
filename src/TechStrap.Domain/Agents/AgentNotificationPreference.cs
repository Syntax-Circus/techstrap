namespace TechStrap.Domain.Agents;

/// <summary>Whether an agent wants an alert when a new ticket arrives for a product (PK: agent and product).</summary>
public sealed record AgentNotificationPreference(Guid AgentId, Guid ProductId, bool NotifyNewTicket);
