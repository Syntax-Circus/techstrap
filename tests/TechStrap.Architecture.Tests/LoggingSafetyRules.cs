using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Sensitive EF logging and Npgsql error detail put row values (emails, names) into log lines and exception messages, which the log redaction
/// cannot rewrite. No source or deployment file may turn either on.
/// </summary>
public static partial class LoggingSafetyRules
{
    [GeneratedRegex(@"EnableSensitiveDataLogging|Include\s*Error\s*Detail|IncludeErrorDetail", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    public static IReadOnlyList<string> FindViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files.Where(f => Forbidden().IsMatch(f.Text)).Select(f => $"{f.Path} turns on sensitive EF logging or Npgsql error detail (PII can reach logs).")];

    /// <summary>Every .cs, .json, .yml, .props and .csproj under src (not bin, obj or node_modules) plus the root compose files and .env examples.</summary>
    public static IEnumerable<(string Path, string Text)> Sources(string repositoryRoot)
    {
        var skipped = new[] { "bin", "obj", "node_modules" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();
        string[] extensions = [".cs", ".json", ".yml", ".yaml", ".props", ".csproj", ".example"];
        var src = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(f.Contains) && (extensions.Contains(Path.GetExtension(f)) || Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal)));
        var root = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetFileName(f).StartsWith("docker-compose", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal));
        return src.Concat(root).Select(f => (Path.GetRelativePath(repositoryRoot, f), File.ReadAllText(f)));
    }
}
