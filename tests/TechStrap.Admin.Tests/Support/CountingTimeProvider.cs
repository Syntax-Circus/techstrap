namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// A <see cref="TimeProvider"/> that lends its clock and timers to another one (the fake clock of <see cref="AdminComponentTest"/>) and counts the timers that are alive. A component that starts a timer and does not release it
/// when it goes leaves <see cref="LiveTimers"/> above zero, which "no call is made after it is gone" cannot see when the component also checks its own disposed flag.
/// </summary>
public sealed class CountingTimeProvider(TimeProvider inner) : TimeProvider
{
    private int _live;

    /// <summary>Timers created through this provider that have not been disposed.</summary>
    public int LiveTimers => Volatile.Read(ref _live);

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _live);
        return new Counted(inner.CreateTimer(callback, state, dueTime, period), this);
    }

    private sealed class Counted(ITimer timer, CountingTimeProvider owner) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

        public void Dispose()
        {
            timer.Dispose();
            Release();
        }

        public ValueTask DisposeAsync()
        {
            Release();
            return timer.DisposeAsync();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._live);
            }
        }
    }
}
