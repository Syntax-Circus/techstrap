using TechStrap.Domain.Agents;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

/// <summary>Maps agents, requesters and tags (D-026).</summary>
internal static class PeopleMappings
{
    public static Agent ToDomain(this AgentRecord record) =>
        Agent.Restore(record.Id, record.OidcSubject, record.Name, record.Email, record.Role, record.IsActive, record.LastSeenAt, record.PublicDisplayName);

    public static AgentRecord ToRecord(this Agent agent)
    {
        var record = new AgentRecord { Id = agent.Id, OidcSubject = agent.OidcSubject };
        agent.CopyTo(record);
        return record;
    }

    public static void CopyTo(this Agent agent, AgentRecord record)
    {
        record.Name = agent.Name;
        record.Email = agent.Email;
        record.Role = agent.Role;
        record.IsActive = agent.IsActive;
        record.LastSeenAt = agent.LastSeenAt;
        record.PublicDisplayName = agent.PublicDisplayName;
    }

    public static AgentNotificationPreference ToDomain(this AgentNotificationPreferenceRecord record) =>
        new(record.AgentId, record.ProductId, record.NotifyNewTicket);

    public static Requester ToDomain(this RequesterRecord record) =>
        Requester.Restore(record.Id, record.Email, record.Name, record.ExternalUserRef, record.ErasedAt, record.Version);

    public static RequesterRecord ToRecord(this Requester requester)
    {
        var record = new RequesterRecord { Id = requester.Id };
        requester.CopyTo(record);
        return record;
    }

    public static void CopyTo(this Requester requester, RequesterRecord record)
    {
        record.Email = requester.Email;
        record.Name = requester.Name;
        record.ExternalUserRef = requester.ExternalUserRef;
        record.ErasedAt = requester.ErasedAt;
    }

    public static Tag ToDomain(this TagRecord record) => Tag.Restore(record.Id, record.Slug, record.Name, record.Colour);

    public static TagRecord ToRecord(this Tag tag)
    {
        var record = new TagRecord { Id = tag.Id, Slug = tag.Slug };
        tag.CopyTo(record);
        return record;
    }

    public static void CopyTo(this Tag tag, TagRecord record)
    {
        record.Name = tag.Name;
        record.Colour = tag.Colour;
    }
}
