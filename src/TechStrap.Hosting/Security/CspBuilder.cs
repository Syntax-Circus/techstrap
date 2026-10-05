using System.Text;
using System.Text.RegularExpressions;

namespace TechStrap.Hosting.Security;

/// <summary>
/// Builds a Content-Security-Policy header value one directive at a time. A directive name and every source are validated, so a value that comes from configuration
/// (an identity-provider origin) can never close the directive early and smuggle in another one: a source may not contain a semicolon, a comma, whitespace or a control
/// character. Directives keep the order they were first added in, so the header is stable and a test can compare it as text.
/// </summary>
public sealed partial class CspBuilder
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, List<string>> _directives = new(StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveName();

    /// <summary>Sets a directive, replacing any earlier sources of the same name. A directive with no source is written as its name alone (<c>upgrade-insecure-requests</c>, <c>sandbox</c>).</summary>
    public CspBuilder Directive(string name, params string[] sources)
    {
        Validate(name, sources);
        if (!_directives.ContainsKey(name))
        {
            _order.Add(name);
        }

        _directives[name] = [.. sources.Distinct(StringComparer.Ordinal)];
        return this;
    }

    /// <summary>Adds sources to a directive, creating it when it does not exist yet. A source that is already there is not repeated.</summary>
    public CspBuilder Allow(string name, params string[] sources)
    {
        Validate(name, sources);
        if (!_directives.TryGetValue(name, out var existing))
        {
            _order.Add(name);
            existing = [];
            _directives[name] = existing;
        }

        foreach (var source in sources.Where(source => !existing.Contains(source, StringComparer.Ordinal)))
        {
            existing.Add(source);
        }

        return this;
    }

    /// <summary>The header value: <c>name source source; name source; ...</c></summary>
    public string Build()
    {
        var text = new StringBuilder();
        foreach (var name in _order)
        {
            if (text.Length > 0)
            {
                text.Append("; ");
            }

            text.Append(name);
            foreach (var source in _directives[name])
            {
                text.Append(' ').Append(source);
            }
        }

        return text.ToString();
    }

    private static void Validate(string name, string[] sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(sources);
        if (!DirectiveName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a CSP directive name (lower-case letters, digits and hyphens).", nameof(name));
        }

        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source) || source.Any(c => c is ';' or ',' || char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new ArgumentException($"A source for '{name}' is empty or holds a semicolon, a comma, whitespace or a control character, which would end the directive or start another.", nameof(sources));
            }
        }
    }
}
