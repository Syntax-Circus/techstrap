using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Agents;

public enum AgentRole
{
    Agent,
    Admin,
}

/// <summary>A support agent, provisioned from the OIDC subject on first sign-in (D-004).</summary>
public sealed class Agent
{
    private Agent(
        Guid id,
        string oidcSubject,
        string? name,
        string email,
        AgentRole role,
        bool isActive,
        DateTimeOffset? lastSeenAt,
        string? publicDisplayName)
    {
        Id = id;
        OidcSubject = oidcSubject;
        Name = name;
        Email = email;
        Role = role;
        IsActive = isActive;
        LastSeenAt = lastSeenAt;
        PublicDisplayName = publicDisplayName;
    }

    public Guid Id { get; }

    public string OidcSubject { get; }

    /// <summary>Full name from the identity provider; may be unknown.</summary>
    public string? Name { get; private set; }

    public string Email { get; private set; }

    public AgentRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>Optional customer-facing name override (D-024). Null means "use the first name".</summary>
    public string? PublicDisplayName { get; private set; }

    public static DomainResult<Agent> Create(string? oidcSubject, string? name, string? email, AgentRole role, TimeProvider clock)
    {
        var subject = Guard.RequiredText(oidcSubject, DomainLimits.NameMaxLength * 2, "oidc-subject");
        var agentName = Guard.OptionalText(name, DomainLimits.NameMaxLength, "name");
        var agentEmail = Guard.Email(email, "email");

        return Guard.FirstError(subject, agentName, agentEmail) is { } error
            ? error
            : DomainResult<Agent>.Ok(new Agent(EntityId.New(clock), subject.Value, agentName.Value, agentEmail.Value, role, true, null, null));
    }

    public static Agent Restore(
        Guid id,
        string oidcSubject,
        string? name,
        string email,
        AgentRole role,
        bool isActive,
        DateTimeOffset? lastSeenAt,
        string? publicDisplayName) =>
        new(id, oidcSubject, name, email, role, isActive, lastSeenAt, publicDisplayName);

    public void ChangeRole(AgentRole role) => Role = role;

    public void SetActive(bool isActive) => IsActive = isActive;

    public void RecordSeen(TimeProvider clock) => LastSeenAt = clock.GetUtcNow();

    /// <summary>Refreshes name and email from the identity provider's claims.</summary>
    public DomainResult UpdateIdentity(string? name, string? email)
    {
        var agentName = Guard.OptionalText(name, DomainLimits.NameMaxLength, "name");
        var agentEmail = Guard.Email(email, "email");
        if (Guard.FirstError(agentName, agentEmail) is { } error)
        {
            return error;
        }

        Name = agentName.Value;
        Email = agentEmail.Value;
        return DomainResult.Ok();
    }

    /// <summary>Trimmed; blank clears the override; rejects over-long text, '@' and control characters (D-024).</summary>
    public DomainResult SetPublicDisplayName(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            PublicDisplayName = null;
            return DomainResult.Ok();
        }

        if (text.Length > DomainLimits.PublicDisplayNameMaxLength)
        {
            return DomainErrors.Validation("public-display-name-too-long", $"A public display name is at most {DomainLimits.PublicDisplayNameMaxLength} characters.", "public-display-name");
        }

        if (text.Contains('@') || text.Any(char.IsControl))
        {
            return DomainErrors.Validation("public-display-name-invalid", "A public display name is plain text without '@'.", "public-display-name");
        }

        PublicDisplayName = text;
        return DomainResult.Ok();
    }
}
