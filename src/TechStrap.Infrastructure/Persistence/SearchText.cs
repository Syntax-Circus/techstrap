using TechStrap.Domain.Rules;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>Normalizes user search text before it becomes a query parameter, for knowledge-base and ticket search alike.</summary>
internal static class SearchText
{
    /// <summary>
    /// Trims the text and caps it at <see cref="DomainLimits.SearchTextMaxLength"/>. A cut that lands between the two halves of a
    /// surrogate pair drops the orphaned high surrogate, which UTF-8 encoding would otherwise reject. Returns null for null input;
    /// blank text becomes an empty string.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var text = raw?.Trim();
        if (text is null || text.Length <= DomainLimits.SearchTextMaxLength)
        {
            return text;
        }

        text = text[..DomainLimits.SearchTextMaxLength];
        return char.IsHighSurrogate(text[^1]) ? text[..^1] : text;
    }
}
