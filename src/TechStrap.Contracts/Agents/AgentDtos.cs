namespace TechStrap.Contracts.Agents;

/// <summary>Role names as they appear in agent DTOs (D-029: derived from IdP groups).</summary>
public static class AgentRoles
{
    public const string Agent = "Agent";
    public const string Admin = "Admin";
}

/// <summary>
/// The customer-facing agent name format (D-024): "{first name or override} from {product} Support". Admin uses it for the
/// profile preview; it matches TechStrap.Domain.Agents.AgentPublicIdentity.Resolve (parity test in Application.Tests).
/// </summary>
public static class AgentPublicName
{
    public const string Format = "{0} from {1} Support";

    public const string FallbackFormat = "{0} Support";
}

/// <summary>The signed-in agent's own profile.</summary>
public sealed record AgentDto(
    Guid Id,
    string? Name,
    string Email,
    string Role,
    bool IsActive,
    string? PublicDisplayName,
    DateTimeOffset? LastSeenAt);

/// <summary>
/// One row of the agent list. Agents see active agents with Id, Name and DisplayLabel only (for assignment); Admins also
/// get Email, Role, IsActive and LastSeenAt (D-022).
/// </summary>
public sealed record AgentListItemDto(
    Guid Id,
    string? Name,
    string DisplayLabel,
    string? Email,
    string? Role,
    bool? IsActive,
    DateTimeOffset? LastSeenAt);

/// <summary>Admin-only. The API cannot change a role (D-029); it only activates or deactivates.</summary>
public sealed record UpdateAgentRequest(bool IsActive);

/// <summary>Sets or clears (null or blank) the customer-facing display name override (D-024).</summary>
public sealed record UpdateMyProfileRequest(string? PublicDisplayName);

/// <summary>Per-product new-ticket alert opt-in for the signed-in agent.</summary>
public sealed record NotificationPreferenceDto(Guid ProductId, string ProductName, bool NotifyNewTicket);

public sealed record NotificationPreferenceUpdateDto(Guid ProductId, bool NotifyNewTicket);

public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceUpdateDto> Preferences);
