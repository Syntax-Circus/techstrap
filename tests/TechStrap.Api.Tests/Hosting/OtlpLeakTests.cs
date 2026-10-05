using Serilog.Events;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// An OTLP exporter configured with a header such as <c>x-api-key</c> must never put that secret in a log. Each test enables OTLP with a header secret, points the exporter at a
/// local listener (<see cref="OtlpProbe"/>) and waits, while the host is still running, for the exporter to connect: that positive control proves an export was really attempted,
/// so "the secret is in no log event" cannot pass just because nothing was ever sent. Then the host is disposed and every event, at every level, is scanned.
/// With the current exporter packages the export does not go through <c>IHttpClientFactory</c> (removing the default logging does not make these tests fail; checked), so the
/// test that proves the factory default itself is <see cref="FactoryClientLeakTests"/>. These tests stay as the end-to-end guard: if an upgrade moves the exporter onto the factory,
/// or any other path starts logging the header, they fail.
/// </summary>
/// <remarks>The tests set process environment variables (the exporter options bind before host settings exist), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class OtlpLeakTests
{
    internal const string Secret = "otlp-secret-0123456789abcdef0123456789abcdef";

    internal static readonly IReadOnlyDictionary<string, string?> VerboseLogging = new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    };

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    /// <summary>Sets the OTLP environment variables for one host start and clears them afterwards. The exporter reads them while Program.cs builds the host.</summary>
    internal static async Task WithOtlpAsync(OtlpProbe probe, Func<Task> run)
    {
        var variables = new Dictionary<string, string>
        {
            ["OpenTelemetry__Enabled"] = "true",
            ["OpenTelemetry__OtlpEndpoint"] = probe.Endpoint,
            ["OpenTelemetry__OtlpProtocol"] = "http/protobuf",
            ["OpenTelemetry__Headers"] = $"x-api-key={Secret}",

            // The batch exporter sends every five seconds by default; this makes the first export happen while the test is still running.
            ["OTEL_BSP_SCHEDULE_DELAY"] = "250",
        };
        foreach (var (key, value) in variables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            await run();
        }
        finally
        {
            foreach (var key in variables.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    /// <summary>
    /// Waits, while the host is still running, until the exporter has connected to the probe, then gives the HttpClient pipeline a moment to write its log events. The wait must
    /// happen before the host is disposed: a flush during shutdown runs after the logger is gone, so it would log nothing and the leak scan below could never fail.
    /// </summary>
    internal static async Task WaitForExportAsync(OtlpProbe probe, CancellationToken cancellationToken)
    {
        (await probe.WaitForConnectionAsync(TimeSpan.FromSeconds(20), cancellationToken))
            .ShouldBeTrue("the host never connected to the OTLP endpoint, so this test would pass without exercising the exporter's HttpClient");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    /// <summary>The scan, run after the host was disposed. The positive control (the exporter connected) was asserted by <see cref="WaitForExportAsync"/> while the host was still running.</summary>
    internal static void AssertNoLeak(CollectingSink sink)
    {
        sink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or the scan only saw Information and above");
        sink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Secret));
    }

    [Fact]
    public async Task An_Api_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        CollectingSink sink = null!;
        await WithOtlpAsync(probe, async () =>
        {
            var factory = new ApiFactory(settings: VerboseLogging);
            sink = factory.LogSink;
            try
            {
                using var client = factory.CreateClient();
                (await client.GetStringAsync("/health/live", ct)).ShouldNotContain(Secret);
                await WaitForExportAsync(probe, ct);
            }
            finally
            {
                await factory.DisposeAsync();
            }
        });

        AssertNoLeak(sink);
    }

    [Fact]
    public async Task A_Portal_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        CollectingSink sink = null!;
        await WithOtlpAsync(probe, async () =>
        {
            var factory = new PortalFactory(settings: VerboseLogging);
            sink = factory.LogSink;
            try
            {
                using var client = factory.CreateClient();
                (await client.GetStringAsync("/", ct)).ShouldNotContain(Secret);
                await WaitForExportAsync(probe, ct);
            }
            finally
            {
                await factory.DisposeAsync();
            }
        });

        AssertNoLeak(sink);
    }
}
