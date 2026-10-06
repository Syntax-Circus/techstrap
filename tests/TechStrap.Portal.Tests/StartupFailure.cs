using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Serilog.Events;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Reads the real start failure of a Portal host that must refuse to start. WebApplicationFactory's deferred host can race its own disposal: when ValidateOnStart fails the app disposes its services and
/// <c>CreateClient()</c> may throw an <see cref="ObjectDisposedException"/> instead of the <see cref="OptionsValidationException"/>. The real exception is then on the "Hosting failed to start" log event.
/// Never passes when the start did not fail on options validation.
/// </summary>
internal static class StartupFailure
{
    private static readonly TimeSpan LogWait = TimeSpan.FromSeconds(5);

    /// <param name="factory">The factory to start, as returned by <c>WithWebHostBuilder</c> when settings were added.</param>
    /// <param name="logSource">The <see cref="PortalFactory"/> it was derived from: only that one owns the <see cref="PortalFactory.LogSink"/> the derived host logs to.</param>
    public static OptionsValidationException Capture(WebApplicationFactory<TechStrap.Portal.Program> factory, PortalFactory logSource) =>
        Capture(() => factory.CreateClient().Dispose(), () => logSource.LogSink.Events, LogWait);

    public static OptionsValidationException Capture(PortalFactory factory) => Capture(factory, factory);

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
