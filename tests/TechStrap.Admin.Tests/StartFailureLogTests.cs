using Serilog.Events;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// <see cref="StartupFailure"/> reads a start failure from the log when <c>CreateClient()</c> loses a race with the disposal of a host that failed options validation. The Admin factory must therefore put
/// its failed start on <c>LogSink</c> as a "Hosting failed to start" event that carries the <c>OptionsValidationException</c>.
/// </summary>
public sealed class StartFailureLogTests
{
    [Fact]
    public async Task The_Admin_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = null });

        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        started.Message.ShouldContain("AUTH__AUTHORITY");

        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, TimeSpan.FromSeconds(5));

        fromLog.Message.ShouldBe(started.Message);
        factory.LogSink.Events.ShouldContain(e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text.Contains("Hosting failed to start"));
    }
}
