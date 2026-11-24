using System.Text;

namespace TechStrap.Portal.Seo;

/// <summary>
/// Pure text rules for page titles and meta descriptions, so no page can ship a missing, too short or too long one. A description is 110 to 160 characters (search engines flag shorter ones as thin and cut longer ones);
/// a title is at most 70. An owner-written description is used when it is long enough, otherwise the generated fallback applies. Generated fallbacks are built from real sentences and always land in range, whatever the
/// product name or category list looks like.
/// </summary>
public static class SeoText
{
    /// <summary>The shortest description accepted as written (shorter ones are replaced by the fallback).</summary>
    public const int DescriptionMinLength = 110;

    /// <summary>The longest description (longer ones are cut on a word boundary).</summary>
    public const int DescriptionMaxLength = 160;

    /// <summary>The longest title (longer ones are cut on a word boundary).</summary>
    public const int TitleMaxLength = 70;

    private const string Ellipsis = "…";

    private static readonly string[] Fillers =
    [
        "Search the help center to get started.",
        "Our support team is here to help if you cannot find what you need.",
    ];

    /// <summary>Trims, collapses every run of whitespace to one space and drops control characters (a null is the empty string).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (char.IsControl(c))
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>The candidate when it is at least <see cref="DescriptionMinLength"/> characters once normalized (cut to <see cref="DescriptionMaxLength"/>), otherwise the fallback (normalized and cut the same way).</summary>
    public static string Description(string? candidate, Func<string> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var text = Normalize(candidate);
        return Clamp(text.Length >= DescriptionMinLength ? text : Normalize(fallback()), DescriptionMaxLength);
    }

    /// <summary>The title normalized and cut to <see cref="TitleMaxLength"/> on a word boundary.</summary>
    public static string Title(string? title) => Clamp(Normalize(title), TitleMaxLength);

    /// <summary>Cuts text longer than <paramref name="maxLength"/> on a word boundary and ends it with an ellipsis; shorter text is returned as it is.</summary>
    public static string Clamp(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= maxLength)
        {
            return text;
        }

        var room = maxLength - Ellipsis.Length;
        var cut = text.LastIndexOf(' ', room);

        // Back up to a word boundary only when that keeps most of the text; one huge word is cut in place, never between the two halves of a surrogate pair (an emoji).
        var end = cut > room / 2 ? cut : room;
        if (end > 0 && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        var head = text[..end].TrimEnd(' ', ',', ';', ':', '-', '.');
        return head + Ellipsis;
    }

    /// <summary>
    /// The description of a product's help-center home: how many articles there are and which topics they cover. The category list is dropped when it would overflow, a count of zero is left out, and a very long product
    /// name still gives a valid result.
    /// </summary>
    public static string KbHomeDescription(string productName, IReadOnlyList<string> categoryNames, int articleCount)
    {
        ArgumentNullException.ThrowIfNull(categoryNames);
        var product = Normalize(productName);
        var names = categoryNames.Select(Normalize).Where(n => n.Length > 0).ToList();
        var lead = $"Find answers, how-to guides and troubleshooting for {product}";
        var articles = articleCount > 0 ? $"browse {articleCount} help {(articleCount == 1 ? "article" : "articles")}" : "search the help center";
        var candidates = new List<string>();
        for (var take = Math.Min(names.Count, 4); take > 0; take--)
        {
            candidates.Add($"{lead}: {articles} across {Join(names.Take(take))}, or contact the support team.");
        }

        candidates.Add($"{lead}: {articles} or contact the support team.");
        return Fit(candidates);
    }

    /// <summary>The description of a product's home page: what the help center offers and how to reach support.</summary>
    public static string ProductHomeDescription(string productName, IReadOnlyList<string> categoryNames)
    {
        ArgumentNullException.ThrowIfNull(categoryNames);
        var product = Normalize(productName);
        var names = categoryNames.Select(Normalize).Where(n => n.Length > 0).ToList();
        var candidates = new List<string>();
        for (var take = Math.Min(names.Count, 4); take > 0; take--)
        {
            candidates.Add($"{product} support: search the help center for answers about {Join(names.Take(take))}, or contact the support team and get a reply by email.");
        }

        candidates.Add($"{product} support: search the help center for answers and how-to guides, or contact the support team and get a reply by email.");
        return Fit(candidates);
    }

    /// <summary>The description of a help-center category: how many articles it holds and what for.</summary>
    public static string CategoryDescription(string categoryName, string productName, int articleCount)
    {
        var category = Normalize(categoryName);
        var product = Normalize(productName);
        var articles = articleCount > 0 ? $"Browse {articleCount} help {(articleCount == 1 ? "article" : "articles")}" : "Browse help articles";
        return Fit(
        [
            $"{articles} about {category} for {product}. Find step-by-step answers and troubleshooting, or contact the support team.",
            $"{articles} about {category} for {product}, or contact the support team.",
            $"{articles} about {category} for {product}.",
        ]);
    }

    /// <summary>The first candidate that is in range; otherwise the longest short one padded with filler sentences, or the shortest long one cut down.</summary>
    private static string Fit(IReadOnlyList<string> candidates)
    {
        string? longestShort = null;
        string? shortestLong = null;
        foreach (var raw in candidates)
        {
            var candidate = Normalize(raw);
            if (candidate.Length is >= DescriptionMinLength and <= DescriptionMaxLength)
            {
                return candidate;
            }

            if (candidate.Length < DescriptionMinLength)
            {
                if (longestShort is null || candidate.Length > longestShort.Length)
                {
                    longestShort = candidate;
                }
            }
            else if (shortestLong is null || candidate.Length < shortestLong.Length)
            {
                shortestLong = candidate;
            }
        }

        if (longestShort is not null)
        {
            var padded = longestShort;
            foreach (var filler in Fillers)
            {
                if (padded.Length >= DescriptionMinLength)
                {
                    break;
                }

                padded = $"{padded} {filler}";
            }

            return Clamp(padded, DescriptionMaxLength);
        }

        return Clamp(shortestLong ?? string.Empty, DescriptionMaxLength);
    }

    private static string Join(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}",
        };
    }
}
