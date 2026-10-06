using Microsoft.AspNetCore.Http;

namespace TechStrap.Hosting.Wiring;

/// <summary>
/// A response-header rule for the requests whose path matches (D-045). <see cref="BrowserHostExtensions.UseTechStrapWebHost(Microsoft.AspNetCore.Builder.WebApplication, IReadOnlyList{PathHeaderRule}, string[])"/>
/// applies the rules after the shared security headers, in the order given, so a rule's value wins over the shared one and over a value the endpoint set itself (the shared middleware
/// would overwrite an endpoint's <c>Referrer-Policy</c> or Content-Security-Policy when the response starts).
/// The path is decided from the request as it arrived, before any re-execution, so the page that replaces a 404 under a matching path carries the rule's headers as well.
/// </summary>
public sealed class PathHeaderRule
{
    private PathHeaderRule(Func<PathString, bool> matches, IReadOnlyList<(string Name, Func<string, string> Change)> changes, bool successOnly)
    {
        Matches = matches;
        Changes = changes;
        SuccessOnly = successOnly;
    }

    /// <summary>True for the request path this rule is for. Called once per request, with the path as it arrived.</summary>
    public Func<PathString, bool> Matches { get; }

    /// <summary>The header changes, in order: the header name and a function from the current value (empty when the header is absent) to the new one.</summary>
    internal IReadOnlyList<(string Name, Func<string, string> Change)> Changes { get; }

    /// <summary>When true the changes are made to a successful (2xx) response only.</summary>
    internal bool SuccessOnly { get; }

    /// <summary>Sets each header to the given value on every response for a matching path, whatever status it has.</summary>
    public static PathHeaderRule Set(Func<PathString, bool> matches, params (string Name, string Value)[] headers) => Create(matches, headers, successOnly: false);

    /// <summary>
    /// Sets each header to the given value on a successful (2xx) response for a matching path only; a 404, a 429, a 503 or a redirect keeps whatever it had. It is for a header that is only true of a delivered page,
    /// such as <c>Cache-Control: public, max-age=60</c>, which must never be put on an error answer (a browser or a proxy would keep it).
    /// </summary>
    public static PathHeaderRule SetOnSuccess(Func<PathString, bool> matches, params (string Name, string Value)[] headers) => Create(matches, headers, successOnly: true);

    private static PathHeaderRule Create(Func<PathString, bool> matches, (string Name, string Value)[] headers, bool successOnly)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Length == 0 || headers.Any(h => string.IsNullOrWhiteSpace(h.Name)))
        {
            throw new ArgumentException("A rule sets at least one header, and every header has a name.", nameof(headers));
        }

        return new PathHeaderRule(matches, [.. headers.Select(h => (h.Name, (Func<string, string>)(_ => h.Value)))], successOnly);
    }

    /// <summary>
    /// Adds a bare <c>sandbox</c> directive to the Content-Security-Policy of a successful (2xx) response for a matching path, so a file that is opened rather than saved cannot run script in the
    /// app's origin. An error answer is an ordinary app page that needs its script, and the full page policy still applies to it.
    /// </summary>
    public static PathHeaderRule Sandbox(Func<PathString, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return new PathHeaderRule(matches, [("Content-Security-Policy", BrowserHostExtensions.WithSandbox)], successOnly: true);
    }
}
