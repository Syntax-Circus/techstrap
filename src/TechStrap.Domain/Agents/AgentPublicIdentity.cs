namespace TechStrap.Domain.Agents;

/// <summary>
/// The name a customer sees for an agent (D-024): "{first name} from {Product} Support". An override replaces the first name
/// and keeps the suffix (Assumption in D-024). An agent without a name is shown as "{Product} Support". Never exposes an
/// email address or surname.
/// </summary>
public static class AgentPublicIdentity
{
    private const string SupportSuffix = "Support";

    public static string Resolve(Agent agent, string productDisplayName)
    {
        var supportName = $"{productDisplayName} {SupportSuffix}";
        var given = !string.IsNullOrWhiteSpace(agent.PublicDisplayName)
            ? agent.PublicDisplayName.Trim()
            : FirstWord(agent.Name);

        return given is null ? supportName : $"{given} from {supportName}";
    }

    private static string? FirstWord(string? fullName)
    {
        var word = fullName?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        // The identity provider may send an email address as the name; it is never shown to a customer.
        return word is not null && word.Contains('@') ? null : word;
    }
}
