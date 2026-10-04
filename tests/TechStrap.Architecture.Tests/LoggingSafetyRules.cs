using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Sensitive EF logging and Npgsql error detail put row values (emails, names) into log lines and exception messages, which the log redaction
/// cannot rewrite. No source or deployment file may turn either on. EF parameter logging is refused for the same reason.
/// The scan is deliberately crude: even an "=false" line or a comment that names one of these options is refused, so the names never appear at all.
/// </summary>
public static partial class LoggingSafetyRules
{
    [GeneratedRegex(@"EnableSensitiveDataLogging|Include\s*Error\s*Detail|IncludeErrorDetail|EnableParameterLogging|parameterLoggingEnabled", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    public static IReadOnlyList<string> FindViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files.Where(f => Forbidden().IsMatch(f.Text)).Select(f => $"{f.Path} turns on sensitive EF logging or Npgsql error detail (PII can reach logs).")];

    /// <summary>
    /// Every .cs, .json, .yml, .yaml, .props, .targets, .csproj and .example file under src (not bin, obj or node_modules), plus at the repository root the
    /// compose files, Dockerfiles, Directory.Build files and .env files, plus .github/workflows. Extensions match case-insensitively.
    /// </summary>
    public static IEnumerable<(string Path, string Text)> Sources(string repositoryRoot)
    {
        var skipped = new[] { "bin", "obj", "node_modules" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();
        string[] extensions = [".cs", ".json", ".yml", ".yaml", ".props", ".targets", ".csproj", ".example"];
        var src = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(f.Contains) && (extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith(".env", StringComparison.OrdinalIgnoreCase)));
        string[] rootPrefixes = ["docker-compose", "compose", "Dockerfile", "Directory.Build", ".env"];
        var root = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(f => rootPrefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        var workflowDirectory = Path.Combine(repositoryRoot, ".github", "workflows");
        var workflows = Directory.Exists(workflowDirectory)
            ? Directory.EnumerateFiles(workflowDirectory, "*", SearchOption.AllDirectories).Where(f => new[] { ".yml", ".yaml" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            : [];
        return src.Concat(root).Concat(workflows).Select(f => (Path.GetRelativePath(repositoryRoot, f), File.ReadAllText(f)));
    }
}
