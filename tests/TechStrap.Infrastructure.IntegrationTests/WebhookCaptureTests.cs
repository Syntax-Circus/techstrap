#if DEBUG
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TechStrap.Contracts.Webhooks;
using TechStrap.Infrastructure.Webhooks;
using TechStrap.Infrastructure.Webhooks.LocalCapture;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The Debug-only switch that sends webhook deliveries to a catcher on this machine. None of it exists in a Release build.</summary>
public sealed class WebhookCaptureTests
{
    private const string CaptureUrl = "http://localhost:55270/capture";

    private static WebhookEventDto Payload => new(1, Guid.NewGuid(), "ticket.created", DateTimeOffset.UtcNow, new(Guid.NewGuid(), "test", "Test"), new(Guid.NewGuid(), "Ticket", "Subject @everyone", "New", null, "TEST-1", "Support", null, null));

    [Fact]
    public async Task Generic_delivery_goes_to_the_capture_url_with_only_the_original_host()
    {
        var transport = new RecordingTransport();
        using var sender = WebhookCaptureRegistration.CreateSender(CaptureUrl, isDevelopment: true, NullLogger.Instance, transport);

        var result = await sender.SendAsync("https://hooks.example.com/services/SECRET?token=x", "Json", Payload, TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        transport.RequestUri.ShouldBe(new Uri(CaptureUrl));
        transport.Headers[WebhookCaptureRegistration.OriginalHostHeader].ShouldBe("hooks.example.com");
        transport.Headers.ShouldContainKey("X-TechStrap-Event-Id");
        transport.RequestUri!.AbsoluteUri.ShouldNotContain("SECRET");
        transport.Headers.Values.ShouldAllBe(value => !value.Contains("SECRET") && !value.Contains("token=x"));
    }

    [Fact]
    public async Task Discord_delivery_keeps_its_Discord_body()
    {
        var transport = new RecordingTransport();
        using var sender = WebhookCaptureRegistration.CreateSender(CaptureUrl, isDevelopment: true, NullLogger.Instance, transport);

        await sender.SendAsync("https://discord.com/api/webhooks/123/SECRET", "Discord", Payload, TestContext.Current.CancellationToken);

        transport.RequestUri.ShouldBe(new Uri(CaptureUrl));
        transport.Headers[WebhookCaptureRegistration.OriginalHostHeader].ShouldBe("discord.com");
        transport.Body.ShouldNotBeNull();
        transport.Body.ShouldContain("allowed_mentions");
    }

    [Theory]
    [InlineData("https://localhost:55270/capture")]
    [InlineData("http://example.com/capture")]
    [InlineData("http://192.168.1.10/capture")]
    [InlineData("http://user:p@localhost/capture")]
    [InlineData("http://localhost/capture?x=1")]
    [InlineData("http://localhost/capture#a")]
    // Names and addresses that only look like this machine.
    [InlineData("http://localhost./capture")]
    [InlineData("http://127.0.0.1.nip.io/capture")]
    [InlineData("http://localhost@example.com/capture")]
    [InlineData("http://[::ffff:192.168.1.10]/capture")]
    [InlineData("not a url")]
    public void Rejected_capture_urls_throw(string captureUrl)
    {
        var failure = Should.Throw<InvalidOperationException>(() => WebhookCaptureRegistration.CreateSender(captureUrl, isDevelopment: true, NullLogger.Instance));

        failure.Message.ShouldContain(WebhookCaptureRegistration.ConfigurationKey);
        failure.Message.ShouldNotContain(captureUrl);
    }

    [Theory]
    [InlineData("http://127.0.0.1:55270/capture")]
    [InlineData("http://[::1]:55270/capture")]
    public void Loopback_addresses_are_accepted(string captureUrl)
    {
        using var sender = WebhookCaptureRegistration.CreateSender(captureUrl, isDevelopment: true, NullLogger.Instance);

        sender.ShouldNotBeNull();
    }

    [Fact]
    public void Capture_outside_Development_throws()
    {
        var failure = Should.Throw<InvalidOperationException>(() => WebhookCaptureRegistration.CreateSender(CaptureUrl, isDevelopment: false, NullLogger.Instance));

        failure.Message.ShouldContain(WebhookCaptureRegistration.ConfigurationKey);
        failure.Message.ShouldNotContain(CaptureUrl);
    }

    [Fact]
    public async Task Private_destinations_stay_invalid_with_capture_on()
    {
        var transport = new RecordingTransport();
        using var sender = WebhookCaptureRegistration.CreateSender(CaptureUrl, isDevelopment: true, NullLogger.Instance, transport);

        var result = await sender.SendAsync("https://127.0.0.1/x", "Json", Payload, TestContext.Current.CancellationToken);

        result.Error.ShouldBe("destination-invalid");
        transport.Calls.ShouldBe(0);
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }

            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.NoContent);
        }
    }
}
#endif
