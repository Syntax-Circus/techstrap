using Microsoft.AspNetCore.Mvc.Testing;
using Serilog.Events;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// <see cref="StartupFailure"/> reads a start failure from the log when <c>CreateClient()</c> loses a race with the disposal of a host that failed options validation. That fallback is only as good as the
/// sink it reads, so each of the four hosts of this project must put its failed start on <c>LogSink</c> as a "Hosting failed to start" event that carries the <c>OptionsValidationException</c>.
/// </summary>
public sealed class StartFailureLogTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static void ProveTheLogHoldsTheFailure<TEntry>(HostFactory<TEntry> factory, string expectedInMessage)
        where TEntry : class
    {
        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        started.Message.ShouldContain(expectedInMessage);

        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, Wait);

        fromLog.Message.ShouldBe(started.Message);
        factory.LogSink.Events.ShouldContain(e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text.Contains("Hosting failed to start"));
    }

    [Fact]
    public async Task The_Api_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { ["RateLimiting:Public:PermitLimit"] = "0" });

        ProveTheLogHoldsTheFailure(factory, "RateLimiting:Public:PermitLimit");
    }

    [Fact]
    public async Task The_Worker_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new WorkerFactory(settings: new Dictionary<string, string?> { ["EmailOutbox:Enabled"] = "true" });

        ProveTheLogHoldsTheFailure(factory, "Email:Smtp:Host");
    }

    [Fact]
    public async Task The_Admin_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = null });

        ProveTheLogHoldsTheFailure(factory, "AUTH__AUTHORITY");
    }

    [Fact]
    public async Task The_Portal_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = "maybe" });

        ProveTheLogHoldsTheFailure(factory, "TECHSTRAP_PORTAL_SHOW_POWERED_BY");
    }
}
