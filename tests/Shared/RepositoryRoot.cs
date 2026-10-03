namespace TechStrap.Tests.Shared;

/// <summary>Locates the repository root from the test output folder (the folder that holds TechStrap.slnx).</summary>
internal static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    public static string Combine(params string[] segments) => Path.Combine([Find(), .. segments]);
}
