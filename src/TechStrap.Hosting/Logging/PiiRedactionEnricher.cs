using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Hosting.Logging;

/// <summary>
/// Rewrites PII-shaped text in every property value before any sink sees the event (D-039): email addresses (also URL-encoded), 43-character access tokens, JWT-shaped bearer tokens and
/// "sha256:" hashes, and (D-045) the value of a <c>name</c>, <c>email</c>, <c>subject</c>, <c>ref</c> or <c>q</c> query parameter: the Portal's contact page is prefilled with the first three, its "received" page carries the
/// protected ticket reference in <c>ref</c> and its suggest adapter the visitor's search text in <c>q</c>, which a request log would otherwise carry.
/// It cannot touch LogEvent.Exception or the template, and it cannot recognise a name by shape; application code never logs either
/// (exceptions are logged by type name, requesters by id).
/// Residual risk, accepted: names cannot be pattern-redacted, and an attached Exception is not rewritten. The worker loops that attach an
/// exception (EmailOutboxWorker, AutoCloseWorker, OutboxRetentionWorker) get Npgsql's default, which hides PostgresException.Detail unless the error-detail connection option is enabled;
/// the LoggingSafety architecture tests guard that nothing sets it. A char[] is captured as single characters and is not redacted.
/// Redaction fails closed: if rewriting a property throws (including a regex timeout) that property becomes "[redaction-failed]".
/// </summary>
public sealed partial class PiiRedactionEnricher : ILogEventEnricher
{
    public const string EmailMarker = "[email]";
    public const string TokenMarker = "[token]";
    public const string HashMarker = "[hash]";
    public const string QueryValueMarker = "[redacted]";
    public const string FailedMarker = "[redaction-failed]";

    private const RegexOptions Options = RegexOptions.CultureInvariant;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    // sha256: plus 64 hex characters, any case.
    private static readonly Regex HashPattern = new(@"sha256:[0-9a-f]{64}", Options | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, MatchTimeout);

    // Three base64url segments starting with the JWT header prefix "eyJ" ({"): a bearer token. Replaced before the 43-character rule so no segment is left behind.
    private static readonly Regex JwtPattern = new(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", Options | RegexOptions.NonBacktracking, MatchTimeout);

    // Deliberately broader than what intake accepts (non-whitespace, non-@ local part and domain with a dot): IDN, IP literals, quoted local parts,
    // single-letter TLDs, underscores. The separator may be a URL-encoded "@" ("%40", any case): a logged path and query can carry a search term. Over-redaction is acceptable; under-redaction is not.
    private static readonly Regex EmailPattern = new(
        @"(?:""[^""\r\n]*""|[^\s@<>()\[\]"",;:]+)(?:@|%40)(?:\[[^\]\s]+\]|[^\s@<>()\[\]"",;:]+\.[^\s@<>()\[\]"",;:]+)",
        Options | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, MatchTimeout);

    // An optional API key prefix plus a 43-character base64url run that is a whole run; "%2F" (an encoded slash) counts as a boundary.
    // Lookarounds rule out NonBacktracking, so this one relies on the timeout.
    private static readonly Regex TokenPattern = new(
        @"(?:(?<![A-Za-z0-9_\-])|(?<=%2[Ff]))(?:ts[kp]_)?[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])",
        Options, MatchTimeout);

    // A "name=value" pair at the start of a text or after "?" or "&": the value runs up to the next "&", "#" or whitespace (a quote is part of a value: O'Brien). The value boundary deliberately runs to "&", "#" or whitespace, so a value
    // with an unusual character in it is masked whole: it over-masks, on the safe side, rather than leave the tail of a name or an address behind. Whether the parameter is one of the personal ones is decided after decoding
    // its name (see RedactQueryValues), so "%6Eame=" and "NAME=" are masked while "username", "filename" and a sentence that says "name=" are not.
    private static readonly Regex QueryPairPattern = new(@"(?<=^|[?&])(?<name>[^=&#?\s""']+)=(?<value>[^&#\s]*)", Options, MatchTimeout);

    private readonly Func<string, string> _redactText;

    public PiiRedactionEnricher()
        : this(RedactText)
    {
    }

    internal PiiRedactionEnricher(Func<string, string> redactText) => _redactText = redactText;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            LogEventPropertyValue redacted;
            try
            {
                redacted = Redact(value);
            }
            catch (Exception)
            {
                redacted = new ScalarValue(FailedMarker);
            }

            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    internal static string RedactText(string text) =>
        TokenPattern.Replace(JwtPattern.Replace(EmailPattern.Replace(HashPattern.Replace(RedactQueryValues(text), HashMarker), EmailMarker), TokenMarker), TokenMarker);

    private static string RedactQueryValues(string text) =>
        QueryPairPattern.Replace(text, match => IsPersonalQueryName(match.Groups["name"].Value) ? $"{match.Groups["name"].Value}={QueryValueMarker}" : match.Value);

    private static bool IsPersonalQueryName(string name)
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

        return decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase)
            || decoded.Equals("subject", StringComparison.OrdinalIgnoreCase) || decoded.Equals("ref", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase);
    }

    private LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var clean = _redactText(text);
                return clean == text ? value : new ScalarValue(clean);
            case ScalarValue { Value: { } other } when IsTextual(other):
                var original = other.ToString();
                if (original is null)
                {
                    return value;
                }

                var cleaned = _redactText(original);
                return cleaned == original ? value : new ScalarValue(cleaned);
            case SequenceValue sequence:
                var elements = sequence.Elements.Select(Redact).ToArray();
                return Same(elements, sequence.Elements) ? value : new SequenceValue(elements);
            case StructureValue structure:
                var properties = structure.Properties.Select(p => new LogEventProperty(p.Name, Redact(p.Value))).ToArray();
                return Same(properties.Select(p => p.Value), structure.Properties.Select(p => p.Value)) ? value : new StructureValue(properties, structure.TypeTag);
            case DictionaryValue dictionary:
                return RedactDictionary(dictionary);
            default:
                return value;
        }
    }

    /// <summary>Anything that is not a primitive, decimal, Guid, date/time type or enum (a Uri, a custom type Serilog kept as an object).</summary>
    private static bool IsTextual(object value) =>
        value is not (Guid or decimal or DateTime or DateTimeOffset or TimeSpan or DateOnly or TimeOnly or Enum) && !value.GetType().IsPrimitive;

    private LogEventPropertyValue RedactDictionary(DictionaryValue dictionary)
    {
        var used = new HashSet<ScalarValue>();
        var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
        var changed = false;
        foreach (var (key, element) in dictionary.Elements)
        {
            var newKey = (ScalarValue)Redact(key);
            // two keys can redact to the same marker; keep both entries instead of throwing (a throw would fail the whole event open)
            if (!used.Add(newKey) && newKey.Value is string text)
            {
                var n = 2;
                ScalarValue candidate;
                do
                {
                    candidate = new ScalarValue($"{text}#{n++}");
                }
                while (!used.Add(candidate));
                newKey = candidate;
            }

            var newValue = Redact(element);
            changed |= !ReferenceEquals(newKey, key) || !ReferenceEquals(newValue, element);
            entries.Add(KeyValuePair.Create(newKey, newValue));
        }

        return changed ? new DictionaryValue(entries) : dictionary;
    }

    private static bool Same(IEnumerable<LogEventPropertyValue> left, IEnumerable<LogEventPropertyValue> right) =>
        left.Zip(right, ReferenceEquals).All(same => same);
}
