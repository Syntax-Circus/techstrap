using Sentry;

namespace TechStrap.Hosting.Sentry;

public static class SentryOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="SensitiveHeaderSentryProcessor"/> for events and transactions, so no host forgets one of the two, and the same for <see cref="SensitiveQuerySentryProcessor"/>
    /// (the text an agent searched for) together with its breadcrumb hook. Call it from the <c>UseSentry</c> callback of every host that serves requests (Api, Admin, and Portal from PHASE-09).
    /// The breadcrumb hook is the one <c>BeforeBreadcrumb</c> callback the options have, so a host that wants its own must call this first and wrap what it needs around it.
    /// </summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var processor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(processor);
        options.AddTransactionProcessor(processor);

        var query = new SensitiveQuerySentryProcessor();
        options.AddEventProcessor(query);
        options.AddTransactionProcessor(query);
        options.SetBeforeBreadcrumb(SensitiveQuerySentryProcessor.ScrubBreadcrumb);
    }
}
