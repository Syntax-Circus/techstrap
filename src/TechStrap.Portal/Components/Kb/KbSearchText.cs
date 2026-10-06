using TechStrap.Contracts.Kb;

namespace TechStrap.Portal.Components.Kb;

/// <summary>The search text the Portal sends on: trimmed and cut at the API's own limit without splitting a surrogate pair. Both the search page and the suggest adapter use it, so they cut alike.</summary>
public static class KbSearchText
{
    public static string Clean(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length <= KbLimits.MaxSearchTextChars)
        {
            return trimmed;
        }

        var cut = trimmed[..KbLimits.MaxSearchTextChars];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }
}
