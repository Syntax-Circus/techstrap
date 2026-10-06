using Microsoft.Extensions.Options;
using Serilog.Events;
using Serilog.Parsing;

namespace TechStrap.Portal.Tests;

/// <summary>Both branches of <see cref="StartupFailure"/>: the direct exception and the disposed-provider race, plus the cases that must fail loudly.</summary>
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
    public void A_start_that_succeeds_fails_the_capture()
    {
        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => { }, () => [], NoWait));
    }
}
