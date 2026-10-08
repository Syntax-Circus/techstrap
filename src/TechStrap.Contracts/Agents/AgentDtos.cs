namespace TechStrap.Contracts.Agents;

/// <summary>Role names as they appear in agent DTOs (D-029: derived from IdP groups).</summary>
public static class AgentRoles
{
    /// <summary>Can work tickets and the knowledge base. Wire value <c>Agent</c>.</summary>
    public const string Agent = "Agent";
    /// <summary>Can also manage products, API keys, agents and settings. Wire value <c>Admin</c>.</summary>
    public const string Admin = "Admin";
}

/// <summary>
/// The customer-facing agent name format (D-024): "{first name or override} from {product} Support". Admin uses it for the
/// profile preview; it matches TechStrap.Domain.Agents.AgentPublicIdentity.Resolve (parity test in Application.Tests).
/// </summary>
public static class AgentPublicName
{
    /// <summary>The name shown for an agent on a product: first name or override, then product name. Format string <c>{0} from {1} Support</c>.</summary>
    public const string Format = "{0} from {1} Support";

    /// <summary>The name used when the agent has no usable name: the product name only. Format string <c>{0} Support</c>.</summary>
    public const string FallbackFormat = "{0} Support";
}

/// <summary>The signed-in agent's own profile.</summary>
/// <param name="Id">The agent's id.</param>
/// <param name="Name">The name from the identity provider; null if it gave none.</param>
/// <param name="Email">The agent's email address.</param>
/// <param name="Role">One of <see cref="AgentRoles"/>.</param>
/// <param name="IsActive">Whether the agent may sign in.</param>
/// <param name="PublicDisplayName">The customer-facing name override, or null to use the first name.</param>
/// <param name="LastSeenAt">When the agent last used the Admin; null if never.</param>
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
/// <param name="Id">The agent's id.</param>
/// <param name="Name">The name from the identity provider; null if it gave none.</param>
/// <param name="DisplayLabel">A label for pickers: the name, or the email when there is no name.</param>
/// <param name="Email">The email address; null unless the caller is an Admin.</param>
/// <param name="Role">One of <see cref="AgentRoles"/>; null unless the caller is an Admin.</param>
/// <param name="IsActive">Whether the agent may sign in; null unless the caller is an Admin.</param>
/// <param name="LastSeenAt">When the agent last used the Admin; null unless the caller is an Admin.</param>
public sealed record AgentListItemDto(
    Guid Id,
    string? Name,
    string DisplayLabel,
    string? Email,
    string? Role,
    bool? IsActive,
    DateTimeOffset? LastSeenAt);

/// <summary>Admin-only. The API cannot change a role (D-029); it only activates or deactivates. IsActive is nullable so an empty body is rejected instead of binding to false.</summary>
/// <param name="IsActive">True to activate the agent, false to deactivate; required.</param>
public sealed record UpdateAgentRequest(bool? IsActive);

/// <summary>Sets or clears (null or blank) the customer-facing display name override (D-024).</summary>
/// <param name="PublicDisplayName">The new override, or null or blank to clear it.</param>
public sealed record UpdateMyProfileRequest(string? PublicDisplayName);

/// <summary>Per-product new-ticket alert opt-in for the signed-in agent.</summary>
/// <param name="ProductId">The product the preference is for.</param>
/// <param name="ProductName">The product's display name.</param>
/// <param name="NotifyNewTicket">Whether the agent is emailed when a new ticket arrives for the product.</param>
public sealed record NotificationPreferenceDto(Guid ProductId, string ProductName, bool NotifyNewTicket);

/// <summary>One product's new-ticket alert setting in an update.</summary>
/// <param name="ProductId">The product the preference is for.</param>
/// <param name="NotifyNewTicket">Whether the agent is emailed when a new ticket arrives for the product.</param>
public sealed record NotificationPreferenceUpdateDto(Guid ProductId, bool NotifyNewTicket);

/// <summary>Replaces the signed-in agent's new-ticket alert settings for the products listed.</summary>
/// <param name="Preferences">The settings to apply, one per product.</param>
public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceUpdateDto> Preferences);
