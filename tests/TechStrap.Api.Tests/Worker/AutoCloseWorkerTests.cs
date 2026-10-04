using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Worker.AutoClose;

namespace TechStrap.Api.Tests.Worker;

public sealed class AutoCloseWorkerTests
{
    private const int Batch = 3;
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(2);

    private readonly FakeTimeProvider _clock = new();
    private readonly SignallingTimeProvider _timeProvider;
    private readonly IAutoCloseSolvedTicketsHandler _handler = Substitute.For<IAutoCloseSolvedTicketsHandler>();
    private int _scopesCreated;

    public AutoCloseWorkerTests() => _timeProvider = new SignallingTimeProvider(_clock);

    private async Task WaitForTimerAsync() =>
        (await _timeProvider.Timers.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBeTrue("The worker never started its wait.");

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

    private AutoCloseWorker CreateWorker(bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAutoCloseSolvedTicketsHandler>(_ =>
        {
            Interlocked.Increment(ref _scopesCreated);
            return _handler;
        });
        var options = Microsoft.Extensions.Options.Options.Create(new AutoCloseOptions { Enabled = enabled, IntervalMinutes = 2, BatchSize = Batch });
        return new AutoCloseWorker(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), options, _timeProvider, new CollectingLogger());
    }

    private static Result<AutoCloseResult> Done(int closed, int conflicts = 0) =>
        Result<AutoCloseResult>.Success(new AutoCloseResult(closed + conflicts, closed, conflicts));

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
    public async Task It_runs_again_immediately_when_the_batch_was_full_and_waits_otherwise()
    {
        var queue = new Queue<Result<AutoCloseResult>>([Done(3), Done(2, 1), Done(1), Done(0)]);
        _handler.HandleAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : Done(0)));
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        // Full (3), full (2 closed + 1 conflict), then a short batch (1): three runs, one scope each, then the wait.
        await WaitForAsync(() => Calls == 3);
        await WaitForTimerAsync();
        Calls.ShouldBe(3);
        _scopesCreated.ShouldBe(3);

        _clock.Advance(_interval);
        await WaitForAsync(() => Calls == 4);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task It_passes_the_stopping_token_and_stops_promptly()
    {
        CancellationToken seen = default;
        _handler.HandleAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            seen = call.Arg<CancellationToken>();
            return Task.FromResult(Done(0));
        });
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(() => Calls == 1);
        await WaitForTimerAsync();
        seen.CanBeCanceled.ShouldBeTrue();

        var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

        (await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))).ShouldBeSameAs(stopping);
        await stopping;
        seen.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unexpected_exception_is_logged_and_the_loop_resumes_after_the_interval()
    {
        _handler.HandleAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<Result<AutoCloseResult>>(new InvalidOperationException("boom")), _ => Task.FromResult(Done(0)));
        var worker = CreateWorker();
        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => Calls == 1);
        await WaitForTimerAsync();
        Calls.ShouldBe(1);

        _clock.Advance(_interval);
        await WaitForAsync(() => Calls == 2);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_disabled_worker_never_runs()
    {
        _handler.HandleAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Done(3)));
        var worker = CreateWorker(enabled: false);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        _clock.Advance(_interval);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Calls.ShouldBe(0);
        _scopesCreated.ShouldBe(0);
    }

    private sealed class CollectingLogger : ILogger<AutoCloseWorker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
