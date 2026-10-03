using TechStrap.Domain.Agents;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class AgentRecord
{
    public Guid Id { get; set; }

    public string OidcSubject { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string Email { get; set; } = string.Empty;

    public AgentRole Role { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public string? PublicDisplayName { get; set; }
}

internal sealed class AgentNotificationPreferenceRecord
{
    public Guid AgentId { get; set; }

    public Guid ProductId { get; set; }

    public bool NotifyNewTicket { get; set; }
}
