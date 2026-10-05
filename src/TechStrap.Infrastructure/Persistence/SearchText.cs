using System.Text;
using TechStrap.Domain.Rules;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>Normalizes user search text before it becomes a query parameter, for knowledge-base and ticket search alike.</summary>
internal static class SearchText
{
    /// <summary>
    /// Removes control characters (a NUL makes Postgres throw 22021) and unpaired surrogates (UTF-8 encoding throws on them), then trims the text and caps it at <see cref="DomainLimits.SearchTextMaxLength"/>. A cut that lands between the two halves of a
    /// surrogate pair drops the orphaned high surrogate, which UTF-8 encoding would otherwise reject. Returns null for null input;
    /// blank text becomes an empty string.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var text = raw is null ? null : Clean(raw).Trim();
        if (text is null || text.Length <= DomainLimits.SearchTextMaxLength)
        {
            return text;
        }

        text = text[..DomainLimits.SearchTextMaxLength];
        return char.IsHighSurrogate(text[^1]) ? text[..^1] : text;
    }

    private static string Clean(string raw)
    {
        var clean = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (char.IsControl(c))
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
            {
                clean.Append(c).Append(raw[++i]);
            }
            else if (!char.IsSurrogate(c))
            {
                clean.Append(c);
            }
        }

        return clean.ToString();
    }
}
