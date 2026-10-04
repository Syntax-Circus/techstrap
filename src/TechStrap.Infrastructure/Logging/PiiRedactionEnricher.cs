using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Infrastructure.Logging;

/// <summary>
/// Rewrites PII-shaped text in every property value before any sink sees the event (D-039): email addresses, 43-character access tokens and
/// "sha256:" hashes. It cannot touch LogEvent.Exception or the template, and it cannot recognise a name; application code never logs either
/// (exceptions are logged by type name, requesters by id).
/// Residual risk, accepted: names cannot be pattern-redacted, and an attached Exception is not rewritten. The worker loops that attach an
/// exception (EmailOutboxWorker, AutoCloseWorker, OutboxRetentionWorker) get Npgsql's default, which hides PostgresException.Detail unless the error-detail connection option is enabled;
/// the LoggingSafety architecture tests guard that nothing sets it.
/// </summary>
public sealed partial class PiiRedactionEnricher : ILogEventEnricher
{
    public const string EmailMarker = "[email]";
    public const string TokenMarker = "[token]";
    public const string HashMarker = "[hash]";

    [GeneratedRegex(@"sha256:[0-9a-fA-F]{64}")]
    private static partial Regex HashPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_\-])[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])")]
    private static partial Regex TokenPattern();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    internal static string RedactText(string text) =>
        TokenPattern().Replace(EmailPattern().Replace(HashPattern().Replace(text, HashMarker), EmailMarker), TokenMarker);

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var clean = RedactText(text);
                return clean == text ? value : new ScalarValue(clean);
            case SequenceValue sequence:
                var elements = sequence.Elements.Select(Redact).ToArray();
                return Same(elements, sequence.Elements) ? value : new SequenceValue(elements);
            case StructureValue structure:
                var properties = structure.Properties.Select(p => new LogEventProperty(p.Name, Redact(p.Value))).ToArray();
                return Same(properties.Select(p => p.Value), structure.Properties.Select(p => p.Value)) ? value : new StructureValue(properties, structure.TypeTag);
            case DictionaryValue dictionary:
                var entries = dictionary.Elements.Select(pair => KeyValuePair.Create((ScalarValue)Redact(pair.Key), Redact(pair.Value))).ToArray();
                return Same(entries.Select(e => (LogEventPropertyValue)e.Key), dictionary.Elements.Keys.Cast<LogEventPropertyValue>()) && Same(entries.Select(e => e.Value), dictionary.Elements.Values)
                    ? value
                    : new DictionaryValue(entries);
            default:
                return value;
        }
    }

    private static bool Same(IEnumerable<LogEventPropertyValue> left, IEnumerable<LogEventPropertyValue> right) =>
        left.Zip(right, ReferenceEquals).All(same => same);
}
