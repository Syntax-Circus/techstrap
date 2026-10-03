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

    public static bool Has(ClaimsPrincipal principal, string claimType, string group) =>
        principal.FindAll(claimType).SelectMany(claim => Split(claim.Value)).Any(value => string.Equals(value, group, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Split(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(text)?.Where(item => item is not null).Select(item => item.Trim()) ?? [];
            }
            catch (JsonException)
            {
                // Not JSON after all: fall through to the delimited form.
            }
        }

        return text.Split(_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
