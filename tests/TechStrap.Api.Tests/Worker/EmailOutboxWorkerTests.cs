using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Email;
using TechStrap.Worker.Outbox;

namespace TechStrap.Api.Tests.Worker;

public sealed class EmailOutboxWorkerTests
{
    private static readonly TimeSpan _poll = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _clock = new();
    private readonly SignallingTimeProvider _timeProvider;
    private readonly IDrainEmailOutboxHandler _handler = Substitute.For<IDrainEmailOutboxHandler>();
    private readonly CollectingLogger _logger = new();
    private int _scopesCreated;

    public EmailOutboxWorkerTests() => _timeProvider = new SignallingTimeProvider(_clock);

    private async Task WaitForTimerAsync() =>
        (await _timeProvider.Timers.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBeTrue("The worker never started its poll wait.");

    /// <summary>Delegates to a FakeTimeProvider and signals every timer the worker creates, so tests advance time only once the wait exists.</summary>
    private sealed class SignallingTimeProvider(FakeTimeProvider inner) : TimeProvider
    {
        public SemaphoreSlim Timers { get; } = new(0);

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            Timers.Release();
            return timer;
        }
    }

    private EmailOutboxWorker CreateWorker(bool enabled = true, string? workerId = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IDrainEmailOutboxHandler>(_ =>
        {
            Interlocked.Increment(ref _scopesCreated);
            return _handler;
        });
        var options = Microsoft.Extensions.Options.Options.Create(new EmailOutboxWorkerOptions { Enabled = enabled, PollIntervalSeconds = 5, WorkerId = workerId });
        return new EmailOutboxWorker(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), options, _timeProvider, _logger);
    }

    private void Returns(params int[] claimedThenZero)
    {
        var queue = new Queue<int>(claimedThenZero);
        _handler.HandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result<DrainResult>.Success(new DrainResult(queue.Count > 0 ? queue.Dequeue() : 0, 0, 0))));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met within 5 seconds.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private int Calls => _handler.ReceivedCalls().Count();

    [Fact]
    public async Task It_keeps_draining_without_waiting_while_batches_are_not_empty()
    {
        Returns(3, 2, 0, 0);
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => Calls == 3);
        await WaitForTimerAsync();
        Calls.ShouldBe(3);

        _clock.Advance(_poll);
        await WaitForAsync(() => Calls == 4);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Each_iteration_gets_a_fresh_scope()
    {
        Returns(3, 2, 0, 0);
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => Calls == 3);

        _scopesCreated.ShouldBe(3);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_unexpected_exception_is_logged_and_the_loop_resumes_after_the_poll_interval()
    {
        _handler.HandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<Result<DrainResult>>(new InvalidOperationException("boom")),
                _ => Task.FromResult(Result<DrainResult>.Success(new DrainResult(0, 0, 0))));
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => Calls == 1);
        await WaitForAsync(() => _logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException));
        await WaitForTimerAsync();
        Calls.ShouldBe(1);

        _clock.Advance(_poll);
        await WaitForAsync(() => Calls == 2);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_failed_result_waits_the_poll_interval()
    {
        _handler.HandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(Result<DrainResult>.Failure(new ResultError("boom", "failed", ResultErrorKind.Conflict))),
                _ => Task.FromResult(Result<DrainResult>.Success(new DrainResult(0, 0, 0))));
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => Calls == 1);
        await WaitForTimerAsync();
        Calls.ShouldBe(1);

        _clock.Advance(_poll);
        await WaitForAsync(() => Calls == 2);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_disabled_worker_never_calls_the_handler()
    {
        Returns(3);
        var worker = CreateWorker(enabled: false);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        _clock.Advance(_poll);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Calls.ShouldBe(0);
        _scopesCreated.ShouldBe(0);
    }

    [Fact]
    public async Task Stopping_cancels_the_wait_promptly()
    {
        Returns(0);
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(() => Calls == 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

        (await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))).ShouldBeSameAs(stopping);
        await stopping;
    }

    [Fact]
    public void A_configured_worker_id_is_used_and_a_blank_one_is_generated_uniquely()
    {
        CreateWorker(workerId: "  w-7 ").WorkerId.ShouldBe("w-7");

        var first = CreateWorker(workerId: " ").WorkerId;
        var second = CreateWorker().WorkerId;

        first.ShouldNotBe(second);
        first.Length.ShouldBeLessThanOrEqualTo(65);
        second.Length.ShouldBeLessThanOrEqualTo(65);
    }

    private sealed class CollectingLogger : ILogger<EmailOutboxWorker>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, Exception? Exception)> _entries = new();

        public IReadOnlyCollection<(LogLevel Level, Exception? Exception)> Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue((logLevel, exception));
    }
}
