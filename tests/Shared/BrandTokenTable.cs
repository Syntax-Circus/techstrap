using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>One colour token of docs/BRAND.md section 12. <see cref="Dark"/> is null for tokens with a single value (the portal tokens).</summary>
internal sealed record BrandToken(string Name, string Light, string? Dark);

/// <summary>
/// Reads the token tables of docs/BRAND.md, the system of record, so the tests compare the compiled CSS with the
/// document itself instead of with a second hand-typed copy of the values.
/// </summary>
internal static partial class BrandTokenTable
{
    [GeneratedRegex("`--([a-z0-9-]+)`")]
    private static partial Regex NamePattern();

    public static IReadOnlyList<BrandToken> Read()
    {
        var tokens = new List<BrandToken>();
        foreach (var line in File.ReadLines(RepositoryRoot.Combine("docs", "BRAND.md")))
        {
            if (!line.StartsWith("| `--", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            var names = NamePattern().Matches(cells[0]);
            if (names.Count != 1)
            {
                continue; // the product accent row names three tokens and carries no value
            }

            var colours = cells.Skip(2).Select(c => c.Trim('`')).Where(CssColor.IsColour).Take(2).ToArray();
            if (colours.Length == 0)
            {
                continue;
            }

            tokens.Add(new BrandToken(names[0].Groups[1].Value, colours[0], colours.Length > 1 ? colours[1] : null));
        }

        return tokens;
    }
}
