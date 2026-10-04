using Sentry;

namespace TechStrap.Hosting.Sentry;

public static class SentryOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="SensitiveHeaderSentryProcessor"/> for events and transactions, so no host forgets one of the two. Call it from the
    /// <c>UseSentry</c> callback of every host that serves requests (Api, Admin, and Portal from PHASE-09).
    /// </summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var processor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(processor);
        options.AddTransactionProcessor(processor);
    }
}
