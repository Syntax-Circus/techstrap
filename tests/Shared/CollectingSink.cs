using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Tests.Shared;

/// <summary>
/// Captures every Serilog event a test host writes so tests can assert on log lines, and so <see cref="StartupFailure"/> can read a start failure from the log. It is public because the host
/// factories of Api.Tests and Portal.Tests expose it.
/// </summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
