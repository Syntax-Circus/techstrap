using System.Text.RegularExpressions;
using Sentry;
using Sentry.Extensibility;
using TechStrap.Hosting.Logging;

namespace TechStrap.Hosting.Sentry;

/// <summary>
/// Masks the text an agent searched for in what Sentry records. The queue keeps its search in the address (<c>/queue/mine?search=...</c>) so a view can be bookmarked, and the same text goes to the
/// API as <c>GET /api/tickets?search=...</c>. A search is often a requester's email address or a subject line, so on an unhandled exception it must not reach Sentry in the request's query string
/// or URL, in a breadcrumb, or in the description of a span. The value becomes <c>[redacted]</c> and the rest of the address is left alone, so the event still shows which page failed.
/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query), and, for the Portal's contact page (D-045), <c>name</c> and <c>email</c> (the prefill, which a customer's own
/// app puts in the address). The Portal's ticket address carries the access token in its path (<c>/t/{token}</c>), so a 43-character token directly under <c>/t/</c> is masked wherever an address appears.
/// Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
/// </summary>
public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
{
    /// <summary>What replaces a masked value.</summary>
    public const string Mask = "[redacted]";

    // Any "name=value" pair at the start of a text or after "?" or "&"; the value runs up to the next "&", "#", a space or a quote. Whether the name is a sensitive one is decided after decoding it
    // (see Scrub), so "?%73earch=x" and "?Search=x" are masked, while "research", "faq" and a "/queue/search" path are not.
    [GeneratedRegex(@"(?<=^|[?&])(?<name>[^=&#?\s""']+)=(?<value>[^&#\s""']*)", RegexOptions.CultureInvariant)]
    private static partial Regex Parameter();

    // The 43-character access token (base64url) right after "/t/" or "/T/": the Portal's ticket address and its attachment address.
    [GeneratedRegex(@"(?<=/[tT]/)[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])", RegexOptions.CultureInvariant)]
    private static partial Regex TicketPathToken();

    private static bool IsSensitiveName(string name)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(name.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            decoded = name;
        }

        return decoded.Equals("search", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase)
            || decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase);
    }

    // A non-sensitive value is looked into once more per level, but only this deep: "last=/queue/mine?search=x" needs one. The bound keeps hostile text such as "x=a=a=a=..." from
    // recursing once per pair (a stack overflow cannot be caught and would end the process).
    private const int MaxNesting = 2;

    /// <summary>The text with the value of every sensitive query parameter masked and every ticket-path token replaced. Null stays null.</summary>
    public static string? Scrub(string? text) => Scrub(string.IsNullOrEmpty(text) ? text : TicketPathToken().Replace(text, PiiRedactionEnricher.TokenMarker), 0);

    private static string? Scrub(string? text, int depth) =>
        string.IsNullOrEmpty(text)
            ? text
            : Parameter().Replace(text, match =>
            {
                var name = match.Groups["name"].Value;
                if (IsSensitiveName(name))
                {
                    return $"{name}={Mask}";
                }

                // A value that is not itself sensitive can still hold an address with a search in it (a cookie "last=/queue/mine?search=x"), so it is scrubbed in its turn, to a fixed depth.
                var value = match.Groups["value"].Value;
                return depth < MaxNesting ? $"{name}={Scrub(value, depth + 1)}" : match.Value;
            });

    public SentryEvent? Process(SentryEvent @event)
    {
        ScrubRequest(@event.Request);
        if (@event.Message is { } message)
        {
            message.Message = Scrub(message.Message);
            message.Formatted = Scrub(message.Formatted);
            if (message.Params is { } parameters)
            {
                message.Params = [.. parameters.Select(parameter => parameter is string text ? Scrub(text)! : parameter)];
            }
        }

        foreach (var (key, value) in @event.Extra.ToList())
        {
            if (value is string text && Scrub(text) != text)
            {
                @event.SetExtra(key, Scrub(text));
            }
        }

        ScrubTags(@event.Tags, (key, value) => @event.SetTag(key, value));
        foreach (var exception in @event.SentryExceptions ?? [])
        {
            exception.Value = Scrub(exception.Value);
        }

        return @event;
    }

    public SentryTransaction? Process(SentryTransaction transaction)
    {
        ScrubRequest(transaction.Request);
        ScrubTags(transaction.Tags, (key, value) => transaction.SetTag(key, value));
        foreach (var span in transaction.Spans)
        {
            span.Description = Scrub(span.Description);
            ScrubTags(span.Tags, (key, value) => span.SetTag(key, value));
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

    private static void ScrubTags(IReadOnlyDictionary<string, string> tags, Action<string, string> set)
    {
        foreach (var (key, value) in tags.ToList())
        {
            var scrubbed = Scrub(value);
            if (scrubbed != value)
            {
                set(key, scrubbed!);
            }
        }
    }

    // The headers too: the browser sends the page it came from as Referer (the Referrer-Policy still allows the full address on a same-origin request), and that address carries the search.
    private static void ScrubRequest(SentryRequest request)
    {
        request.QueryString = Scrub(request.QueryString);
        request.Url = Scrub(request.Url);
        request.Cookies = Scrub(request.Cookies);
        if (request.Data is string body)
        {
            request.Data = Scrub(body);
        }

        foreach (var (name, value) in request.Headers.ToList())
        {
            var scrubbed = Scrub(value);
            if (scrubbed != value)
            {
                request.Headers[name] = scrubbed!;
            }
        }
    }
}
