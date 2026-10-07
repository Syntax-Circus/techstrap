using Microsoft.Extensions.Options;
using Serilog.Events;
using Serilog.Parsing;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Both branches of <see cref="StartupFailure"/> (the helper itself is shared with the Admin and Api tests): the direct exception and the disposed-provider race, plus the cases that must fail loudly, and
/// the Portal factory's sink as the source of the fallback.
/// </summary>
public sealed class StartupFailureTests
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;
    private static readonly OptionsValidationException Validation = new("n", typeof(object), ["bad value"]);

    private static LogEvent Event(LogEventLevel level, string template, Exception? exception) =>
        new(DateTimeOffset.UtcNow, level, exception, new MessageTemplateParser().Parse(template), []);

    [Fact]
    public void A_direct_OptionsValidationException_is_returned()
    {
        StartupFailure.Capture(() => throw Validation, () => [], NoWait).ShouldBeSameAs(Validation);
    }

    [Fact]
    public void A_disposed_provider_falls_back_to_the_hosting_failed_to_start_event()
    {
        var events = new[]
        {
            Event(LogEventLevel.Error, "Hosting failed to start", new InvalidOperationException("outer", Validation)),
        };

        StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => events, NoWait).ShouldBeSameAs(Validation);
    }

    [Fact]
    public void A_disposed_provider_with_no_log_evidence_fails_clearly()
    {
        var events = new[] { Event(LogEventLevel.Error, "Hosting failed to start", new InvalidOperationException("something else")) };

        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => events, NoWait))
            .Message.ShouldContain("did not fail on validation");
        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => [], NoWait));
    }

    [Fact]
    public void A_log_event_below_Error_or_about_something_else_is_not_evidence()
    {
        var events = new[]
        {
            Event(LogEventLevel.Warning, "Hosting failed to start", new InvalidOperationException("outer", Validation)),
            Event(LogEventLevel.Error, "Something else failed", new InvalidOperationException("outer", Validation)),
        };

        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => events, NoWait))
            .Message.ShouldContain("did not fail on validation");
    }

    [Fact]
    public async Task The_Portal_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = "maybe" });

        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        started.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");

        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, TimeSpan.FromSeconds(5));

        fromLog.Message.ShouldBe(started.Message);
    }

    [Fact]
    public void A_start_that_succeeds_fails_the_capture()
    {
        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => { }, () => [], NoWait));
    }
}
