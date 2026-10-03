using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// ticket_events and admin_events are append-only. EF's change-tracker interceptor cannot see bulk <c>ExecuteUpdate</c> or
/// <c>ExecuteDelete</c> or raw SQL, so no Infrastructure source file may combine those with the two event records or tables.
/// A source scan: coarse on purpose, and a file that really needs the combination must be reviewed rather than slip through.
/// </summary>
public static partial class AppendOnlyBypassRules
{
    [GeneratedRegex("ExecuteUpdate|ExecuteDelete", RegexOptions.CultureInvariant)]
    private static partial Regex BulkOperation();

    [GeneratedRegex("TicketEventRecord|AdminEventRecord|ticket_events|admin_events", RegexOptions.CultureInvariant)]
    private static partial Regex EventTarget();

    [GeneratedRegex("\\b(UPDATE|DELETE\\s+FROM|TRUNCATE(\\s+TABLE)?)\\s+\"?(ticket_events|admin_events)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RawWrite();

    [GeneratedRegex("^\\s*//.*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex CommentLine();

    public static IReadOnlyList<string> FindViolations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        foreach (var (path, source) in files)
        {
            var text = CommentLine().Replace(source, string.Empty);
            if (BulkOperation().IsMatch(text) && EventTarget().IsMatch(text))
            {
                violations.Add($"{path} combines ExecuteUpdate/ExecuteDelete with an append-only event table.");
            }

            if (RawWrite().IsMatch(text))
            {
                violations.Add($"{path} writes to an append-only event table with raw SQL.");
            }
        }

        return violations;
    }

    public static IEnumerable<(string Path, string Text)> InfrastructureSources(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "src", "TechStrap.Infrastructure");
        var skipped = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" };
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !skipped.Any(file.Contains))
            .Select(file => (Path.GetRelativePath(repositoryRoot, file), File.ReadAllText(file)));
    }
}
