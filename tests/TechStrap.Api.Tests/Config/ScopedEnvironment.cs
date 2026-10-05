namespace TechStrap.Api.Tests.Config;

/// <summary>
/// Sets (or, with a null value, removes) process environment variables and puts every one of them back on dispose. Only for tests in
/// <see cref="ProcessEnvironmentCollection"/>: every host built meanwhile reads them. The static <c>HostFactory</c> constructor leaves the test issuer in the
/// environment for the whole run, so a test that needs a host without it removes it here and restores it afterwards.
/// </summary>
public sealed class ScopedEnvironment : IDisposable
{
    private readonly List<(string Name, string? Original)> _originals = [];

    public ScopedEnvironment(params (string Name, string? Value)[] variables)
    {
        foreach (var (name, value) in variables)
        {
            _originals.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in Enumerable.Reverse(_originals))
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
