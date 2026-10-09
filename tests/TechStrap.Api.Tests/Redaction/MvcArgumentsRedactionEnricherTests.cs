using Serilog;
using Serilog.Core;
using Serilog.Events;
using TechStrap.Hosting.Logging;

namespace TechStrap.Api.Tests.Redaction;

public sealed class MvcArgumentsRedactionEnricherTests
{
    private sealed class Sink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static LogEvent Emit(string? sourceContext, bool withArguments)
    {
        var sink = new Sink();
        var log = new LoggerConfiguration().MinimumLevel.Verbose().Enrich.With<MvcArgumentsRedactionEnricher>().WriteTo.Sink(sink).CreateLogger();
        var contextual = sourceContext is null ? log : log.ForContext(Constants.SourceContextPropertyName, sourceContext);
        if (withArguments)
        {
            contextual.Debug("Executing action method {ActionName} with arguments ({Arguments})", "Submit", new[] { "SubmitTicketRequest { Name = Zelda Quillfeather }" });
        }
        else
        {
            contextual.Debug("Executing action method {ActionName}", "Submit");
        }

        return sink.Events.Single();
    }

    [Fact]
    public void The_arguments_of_an_MVC_action_invoker_event_are_replaced_and_the_action_name_stays()
    {
        var logEvent = Emit("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", withArguments: true);

        logEvent.RenderMessage().ShouldNotContain("Zelda");
        logEvent.RenderMessage().ShouldContain(MvcArgumentsRedactionEnricher.ArgumentsMarker);
        logEvent.RenderMessage().ShouldContain("Submit");
        logEvent.Properties["Arguments"].ToString().ShouldNotContain("Zelda");
    }

    [Fact]
    public void An_unrelated_event_with_an_Arguments_property_is_untouched()
    {
        var logEvent = Emit("TechStrap.Something.Else", withArguments: true);

        logEvent.RenderMessage().ShouldContain("Zelda Quillfeather");
    }

    [Fact]
    public void An_MVC_event_without_the_property_or_without_a_source_context_is_a_no_op()
    {
        Emit("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", withArguments: false).Properties.ContainsKey("Arguments").ShouldBeFalse();
        Emit(null, withArguments: true).RenderMessage().ShouldContain("Zelda Quillfeather");
    }
}
