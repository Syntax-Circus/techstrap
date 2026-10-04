using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tests.Email;

public sealed class PurgeEmailOutboxHandlerTests
{
    [Fact]
    public async Task It_deletes_rows_older_than_the_configured_days_in_one_batch()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var store = Substitute.For<IEmailOutboxStore>();
        store.DeleteFinishedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(7);
        var handler = new PurgeEmailOutboxHandler(store, clock,
            Options.Create(new OutboxRetentionOptions { Days = 30, BatchSize = 100 }), NullLogger<PurgeEmailOutboxHandler>.Instance);

        var result = await handler.HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new PurgeEmailOutboxResult(7));
        await store.Received(1).DeleteFinishedBeforeAsync(clock.GetUtcNow() - TimeSpan.FromDays(30), 100, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_store()
    {
        using var source = new CancellationTokenSource();
        var store = Substitute.For<IEmailOutboxStore>();
        var handler = new PurgeEmailOutboxHandler(store, new FakeTimeProvider(),
            Options.Create(new OutboxRetentionOptions()), NullLogger<PurgeEmailOutboxHandler>.Instance);

        await handler.HandleAsync(source.Token);

        await store.Received(1).DeleteFinishedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), source.Token);
    }
}
