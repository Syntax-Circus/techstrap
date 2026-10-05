using System.Text.RegularExpressions;
using Sentry;
using Sentry.Extensibility;

namespace TechStrap.Hosting.Sentry;

/// <summary>
/// Masks the text an agent searched for in what Sentry records. The queue keeps its search in the address (<c>/queue/mine?search=...</c>) so a view can be bookmarked, and the same text goes to the
/// API as <c>GET /api/tickets?search=...</c>. A search is often a requester's email address or a subject line, so on an unhandled exception it must not reach Sentry in the request's query string
/// or URL, in a breadcrumb, or in the description of a span. The value becomes <c>[redacted]</c> and the rest of the address is left alone, so the event still shows which page failed.
/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query). Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
/// </summary>
public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
{
    /// <summary>What replaces a masked value.</summary>
    public const string Mask = "[redacted]";

    // A parameter at the start of a query string or after "?" or "&", up to the next "&", "#", a space or a quote: "search=a%40b.test", "?search=a&page=2", "http://h/queue?q=x#top".
    [GeneratedRegex(@"(?<=^|[?&])(?<name>search|q)=[^&#\s""']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveParameter();

    /// <summary>The text with the value of every sensitive query parameter masked. Null stays null.</summary>
    public static string? Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? text : SensitiveParameter().Replace(text, match => $"{match.Groups["name"].Value}={Mask}");

    public SentryEvent? Process(SentryEvent @event)
    {
        ScrubRequest(@event.Request);
        return @event;
    }

    public SentryTransaction? Process(SentryTransaction transaction)
    {
        ScrubRequest(transaction.Request);
        foreach (var span in transaction.Spans)
        {
            span.Description = Scrub(span.Description);
            foreach (var (key, value) in span.Data.ToList())
            {
                if (value is string text && Scrub(text) != text)
                {
                    span.SetData(key, Scrub(text));
                }
            }
        }

        return transaction;
    }

    /// <summary>
    /// The <c>BeforeBreadcrumb</c> hook: a breadcrumb is immutable, so one that carries a search is replaced by a masked copy. The HTTP breadcrumbs of the API client carry the address in
    /// <c>url</c> and the query in <c>http.query</c>; a log breadcrumb carries it in the message. The hook runs when the breadcrumb is added, so the copy's timestamp is the same moment.
    /// </summary>
    public static Breadcrumb? ScrubBreadcrumb(Breadcrumb breadcrumb, SentryHint hint)
    {
        ArgumentNullException.ThrowIfNull(breadcrumb);
        var message = Scrub(breadcrumb.Message);
        var data = breadcrumb.Data?.ToDictionary(pair => pair.Key, pair => Scrub(pair.Value) ?? string.Empty);
        var changed = message != breadcrumb.Message || (data is not null && breadcrumb.Data!.Any(pair => data[pair.Key] != pair.Value));
        return changed
            ? new Breadcrumb(message!, breadcrumb.Type!, data, breadcrumb.Category, breadcrumb.Level)
            : breadcrumb;
    }

    private static void ScrubRequest(SentryRequest request)
    {
        request.QueryString = Scrub(request.QueryString);
        request.Url = Scrub(request.Url);
    }
}
