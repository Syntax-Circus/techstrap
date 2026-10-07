using Microsoft.Extensions.Logging;

namespace TechStrap.Admin.Tests.Live;

/// <summary>Keeps every formatted message (and the exception of each entry), so a test can prove what was logged and what never was.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }
}
