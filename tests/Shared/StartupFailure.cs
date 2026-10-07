using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Serilog.Events;

namespace TechStrap.Tests.Shared;

/// <summary>
/// Reads the real start failure of a host (Admin, Api, Worker or Portal) that must refuse to start. WebApplicationFactory's deferred host can race its own disposal: when ValidateOnStart fails the app disposes its services and
/// <c>CreateClient()</c> may throw an <see cref="ObjectDisposedException"/> instead of the <see cref="OptionsValidationException"/>. The real exception is then on the "Hosting failed to start" log event.
/// Never passes when the start did not fail on options validation.
/// </summary>
internal static class StartupFailure
{
    private static readonly TimeSpan LogWait = TimeSpan.FromSeconds(5);

    /// <summary>Starts <paramref name="factory"/> and returns the <see cref="OptionsValidationException"/> that stopped it.</summary>
    /// <param name="factory">The factory to start, as returned by <c>WithWebHostBuilder</c> when settings were added.</param>
    /// <param name="events">
    /// The events the host logged, read from the sink of the factory the host was derived from (a factory made by <c>WithWebHostBuilder</c> logs to the root factory's sink only), for example
    /// <c>() => root.LogSink.Events</c>.
    /// </param>
    public static OptionsValidationException Capture<TEntry>(WebApplicationFactory<TEntry> factory, Func<IReadOnlyCollection<LogEvent>> events)
        where TEntry : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Capture(() => factory.CreateClient().Dispose(), events, LogWait);
    }

    internal static OptionsValidationException Capture(Action start, Func<IReadOnlyCollection<LogEvent>> events, TimeSpan logWait)
    {
        try
        {
            start();
        }
        catch (OptionsValidationException direct)
        {
            return direct;
        }
        catch (ObjectDisposedException disposed)
        {
            return FromLog(events, logWait, disposed);
        }

        throw new InvalidOperationException("The host started, but its start was expected to fail on options validation.");
    }

    private static OptionsValidationException FromLog(Func<IReadOnlyCollection<LogEvent>> events, TimeSpan wait, ObjectDisposedException disposed)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            foreach (var logEvent in events())
            {
                if (logEvent.Level >= LogEventLevel.Error && logEvent.MessageTemplate.Text.Contains("Hosting failed to start", StringComparison.Ordinal) && Find(logEvent.Exception) is { } found)
                {
                    return found;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("CreateClient threw ObjectDisposedException and no 'Hosting failed to start' log event carries an OptionsValidationException, so the start did not fail on validation.", disposed);
            }

            Thread.Sleep(50);
        }
    }

    private static OptionsValidationException? Find(Exception? exception) => exception switch
    {
        null => null,
        OptionsValidationException found => found,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(e => e is not null),
        _ => Find(exception.InnerException),
    };
}
