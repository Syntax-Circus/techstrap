using System.Security.Claims;
using System.Text.Json;

namespace TechStrap.Api.Security;

/// <summary>
/// Reads group membership from a principal. IdPs deliver groups as repeated claims, as one JSON array string, or as a
/// comma- or space-separated list; all three are accepted, and names compare without case.
/// </summary>
public static class AgentGroups
{
    private static readonly char[] _separators = [',', ' ', '\t', '\n', '\r'];

    public static bool Has(ClaimsPrincipal principal, string claimType, string group)
    {
        var wanted = group.Trim();
        return principal.FindAll(claimType).Any(claim => Matches(claim.Value, wanted));
    }

    // Order matters: a group name may itself contain spaces or commas, so the whole value is compared first, then JSON
    // array elements, and only then the delimited split.
    private static bool Matches(string value, string wanted)
    {
        var text = value.Trim();
        if (string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.StartsWith('['))
        {
            try
            {
                var items = JsonSerializer.Deserialize<string[]>(text) ?? [];
                return items.Any(item => item is not null && string.Equals(item.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException)
            {
                // Not JSON after all: fall through to the delimited form.
            }
        }

        return text.Split(_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, wanted, StringComparison.OrdinalIgnoreCase));
    }
}
