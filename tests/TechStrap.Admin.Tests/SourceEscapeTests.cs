using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// A unicode escape once lost its backslash (and then its <c>u</c>) while being written by a tool, so a string read "Saving2026" and the tests were written to match.
/// This guard flags a unicode escape without its backslash, or a letter glued to an escape's hex digits at the end of a string literal.
/// </summary>
public sealed partial class SourceEscapeTests
{
    private static readonly string[] Extensions = [".cs", ".razor"];

    [GeneratedRegex(@"(?<!\\)u20[0-9A-Fa-f]{2}(?![0-9A-Za-z])|(?<!\\)[A-Za-z]20(?:1[0-9A-Fa-f]|2[0-9A-Fa-f])\x22(?![A-Za-z0-9])")]
    private static partial Regex Broken();

    [Fact]
    public void No_source_string_has_a_unicode_escape_that_lost_its_backslash()
    {
        var separator = Path.DirectorySeparatorChar;
        var bad = new List<string>();

        foreach (var root in new[] { "src", "tests" })
        {
            var directory = RepositoryRoot.Combine(root);
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || Path.GetFileName(file) == nameof(SourceEscapeTests) + ".cs"
                    || file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}")
                    || file.Contains($"{separator}node_modules{separator}") || file.Contains($"{separator}lib{separator}"))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (Broken().IsMatch(lines[i]))
                    {
                        bad.Add($"{Path.GetRelativePath(directory, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        bad.ShouldBeEmpty();
    }
}
