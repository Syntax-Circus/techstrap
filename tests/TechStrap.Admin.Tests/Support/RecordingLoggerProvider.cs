using Microsoft.Extensions.Logging;

namespace TechStrap.Admin.Tests.Support;

/// <summary>Collects every log line a test run writes (message, exception and structured values), so a test can prove a secret never reached a log.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }

    private sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
            owner.Add($"{category} {logLevel} {formatter(state, exception)} {values} {exception}");
        }
    }
}
