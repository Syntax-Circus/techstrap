using Sentry;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Api.Tests;

public sealed class SentryOptionsExtensionsTests
{
    [Fact]
    public void Scrubbing_registers_the_header_processor_for_events_and_for_transactions()
    {
        var options = new SentryOptions();

        options.AddSensitiveHeaderScrubbing();

        options.GetAllEventProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        options.GetAllTransactionProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Scrubbing_also_registers_the_search_processor_for_events_and_for_transactions()
    {
        var options = new SentryOptions();

        options.AddSensitiveHeaderScrubbing();

        options.GetAllEventProcessors().OfType<SensitiveQuerySentryProcessor>().ShouldHaveSingleItem();
        options.GetAllTransactionProcessors().OfType<SensitiveQuerySentryProcessor>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Scrubbing_needs_options()
    {
        Should.Throw<ArgumentNullException>(() => SentryOptionsExtensions.AddSensitiveHeaderScrubbing(null!));
    }
}
