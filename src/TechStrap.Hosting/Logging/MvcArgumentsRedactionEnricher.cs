using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Hosting.Logging;

/// <summary>
/// ASP.NET Core MVC logs "Executing action method {ActionName} with arguments ({Arguments})" at Trace (Serilog Verbose; observed in the Verbose erase test), and the argument list renders every bound request record (a requester's name, a ticket's subject and body).
/// At any level, for an event from an MVC source, this replaces the <c>Arguments</c> property with <see cref="ArgumentsMarker"/> so the action name stays visible and the bound values never reach a sink.
/// Fails closed: if the property cannot be rewritten it is removed.
/// </summary>
public sealed class MvcArgumentsRedactionEnricher : ILogEventEnricher
{
    public const string ArgumentsMarker = "[arguments]";

    private const string ArgumentsProperty = "Arguments";
    private const string MvcSourcePrefix = "Microsoft.AspNetCore.Mvc";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var source)
            || source is not ScalarValue { Value: string context }
            || !context.StartsWith(MvcSourcePrefix, StringComparison.Ordinal)
            || !logEvent.Properties.ContainsKey(ArgumentsProperty))
        {
            return;
        }

        try
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(ArgumentsProperty, new ScalarValue(ArgumentsMarker)));
        }
        catch (Exception)
        {
            logEvent.RemovePropertyIfPresent(ArgumentsProperty);
        }
    }
}
