namespace TechStrap.Tests.Shared;

/// <summary>
/// The shared XSS corpus (Fixtures/xss-corpus.txt, copied beside the test assembly): one hostile string per line, run through every
/// sanitiser and renderer so they are held to one list.
/// </summary>
public static class XssCorpus
{
    private static readonly Lazy<IReadOnlyList<string>> _vectors = new(Load);

    /// <summary>Every vector, in file order; comment and blank lines are skipped.</summary>
    public static IReadOnlyList<string> Vectors => _vectors.Value;

    /// <summary>One theory row per vector, displayed as <c>vector-NN</c>.</summary>
    public static IEnumerable<TheoryDataRow<string>> Rows() =>
        Vectors.Select((vector, index) => new TheoryDataRow<string>(vector) { TestDisplayName = $"vector-{index + 1:00}" });

    private static IReadOnlyList<string> Load() =>
    [
        .. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xss-corpus.txt"))
            .Where(line => line.Length > 0 && line[0] != '#'),
    ];
}
