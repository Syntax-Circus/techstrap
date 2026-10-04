using System.Text;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>A cp1252 byte slipped into a source file once (an ellipsis became 0x85) and the compiler turned it into U+FFFD, so tests passed on the wrong character.</summary>
public sealed class SourceEncodingTests
{
    private static readonly string[] Extensions = [".cs", ".razor", ".scss", ".js", ".md"];

    [Fact]
    public void Every_source_file_is_strict_utf8()
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        var separator = Path.DirectorySeparatorChar;
        var bad = new List<string>();

        foreach (var root in new[] { "src", "tests" })
        {
            var directory = RepositoryRoot.Combine(root);
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}")
                    || file.Contains($"{separator}node_modules{separator}") || file.Contains($"{separator}lib{separator}"))
                {
                    continue;
                }

                try
                {
                    strict.GetString(File.ReadAllBytes(file));
                }
                catch (DecoderFallbackException)
                {
                    bad.Add(Path.GetRelativePath(directory, file));
                }
            }
        }

        bad.ShouldBeEmpty();
    }
}
