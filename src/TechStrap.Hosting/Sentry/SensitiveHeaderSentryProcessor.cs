using Sentry;
using Sentry.Extensibility;

namespace TechStrap.Hosting.Sentry;

/// <summary>
/// Removes credential-bearing request headers from Sentry events and transactions. Sentry strips only Cookie and Authorization by default, so the
/// customer access token and the intake API key would otherwise reach Sentry on an unhandled exception.
/// </summary>
public sealed class SensitiveHeaderSentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
{
    private static readonly string[] SensitiveHeaders = ["X-Ticket-Token", "X-Api-Key", "Authorization", "Cookie"];

    public SentryEvent? Process(SentryEvent @event)
    {
        Scrub(@event.Request.Headers);
        return @event;
    }

    public SentryTransaction? Process(SentryTransaction transaction)
    {
        Scrub(transaction.Request.Headers);
        return transaction;
    }

    private static void Scrub(IDictionary<string, string> headers)
    {
        foreach (var key in headers.Keys.Where(key => SensitiveHeaders.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            headers.Remove(key);
        }
    }
}
