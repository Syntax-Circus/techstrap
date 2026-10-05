using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The direct proof behind <c>AddTechStrapHttpClientDefaults</c>: a client the factory creates, sending a header that carries a secret, never has that header written to a
/// log, at any level, in any host. The request really goes out (a local listener sees the connection), so the test cannot pass because nothing was sent. The first test is the
/// negative control: the same request through a plain factory with the default logging does put the header value into the log state at Trace (the formatted text shows
/// <c>x-api-key: *</c>, but the structured state carries the value as a string array), so the check is capable of failing.
/// </summary>
public sealed class FactoryClientLeakTests
{
    private const string Secret = "factory-secret-0123456789abcdef0123456789abcdef";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    /// <summary>Sends one request with the secret header to the probe, which accepts the connection and never answers, so the request is abandoned after a moment.</summary>
    private static async Task SendAsync(IHttpClientFactory factory, OtlpProbe probe, CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("secret-header-probe");
        client.DefaultRequestHeaders.Add("x-api-key", Secret);
        using var request = new HttpRequestMessage(HttpMethod.Post, probe.Endpoint) { Content = new ByteArrayContent([1, 2, 3]) };
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        abandon.CancelAfter(TimeSpan.FromSeconds(2));
        await Should.ThrowAsync<OperationCanceledException>(() => client.SendAsync(request, abandon.Token));
        (await probe.WaitForConnectionAsync(TimeSpan.FromSeconds(10), cancellationToken)).ShouldBeTrue("nothing connected, so no request was sent and this test proves nothing");
    }

    [Fact]
    public async Task Control_a_plain_factory_with_the_default_logging_puts_the_header_value_in_the_log_state_at_Trace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        var lines = new List<string>();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new LineProvider(lines)))
            .AddHttpClient("secret-header-probe").Services
            .BuildServiceProvider();

        await SendAsync(services.GetRequiredService<IHttpClientFactory>(), probe, ct);

        lock (lines)
        {
            lines.ShouldContain(line => line.Contains(Secret, StringComparison.Ordinal), "the factory's default logging must leak here, or the host checks below cannot fail");
        }
    }

    [Fact]
    public async Task The_Api_never_logs_a_factory_clients_header()
    {
        await using var factory = new ApiFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Worker_never_logs_a_factory_clients_header()
    {
        await using var factory = new WorkerFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Admin_never_logs_a_factory_clients_header()
    {
        await using var factory = new AdminFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Portal_never_logs_a_factory_clients_header()
    {
        await using var factory = new PortalFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    private static async Task AssertNoLeakAsync(IServiceProvider services, CollectingSink sink)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();

        await SendAsync(services.GetRequiredService<IHttpClientFactory>(), probe, ct);

        sink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or the scan only saw Information and above");
        sink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Secret));
    }

    /// <summary>Records every line a logger writes, including the structured state's own text (where the factory puts the header values).</summary>
    private sealed class LineProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recorder(lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> lines) : ILogger
        {
            // The factory writes the header values as a string array in the structured state ("x-api-key" = [value]), so the array's contents must be read, not its type name.
            private static string Text(object? value) => value switch
            {
                string text => text,
                System.Collections.IEnumerable items => string.Join(',', items.Cast<object?>().Select(Text)),
                _ => value?.ToString() ?? string.Empty,
            };

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={Text(p.Value)}")) : string.Empty;
                lock (lines)
                {
                    lines.Add($"{logLevel} {formatter(state, exception)} {values} {exception}");
                }
            }
        }
    }
}
