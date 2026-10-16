using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Webhooks;
using TechStrap.Infrastructure.Webhooks;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class WebhookSenderTests
{
    private static WebhookEventDto Payload => new(1, Guid.NewGuid(), "ticket.created", DateTimeOffset.UtcNow, new(Guid.NewGuid(), "test", "Test"), new(Guid.NewGuid(), "Ticket", "Subject @everyone", "New", null, "TEST-1", "Support", null, null));

    [Theory]
    [InlineData(200, true, false)]
    [InlineData(204, true, false)]
    [InlineData(302, false, false)]
    [InlineData(400, false, false)]
    [InlineData(401, false, false)]
    [InlineData(404, false, false)]
    [InlineData(408, false, true)]
    [InlineData(429, false, true)]
    [InlineData(503, false, true)]
    public async Task Http_statuses_are_classified_without_exposing_remote_response_bodies(int status, bool success, bool retryable)
    {
        using var sender = new WebhookSender(new ResponseHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("sensitive-remote-body") }));
        var result = await sender.SendAsync("https://example.com/hook", "Json", Payload, TestContext.Current.CancellationToken);
        result.Success.ShouldBe(success);
        result.Retryable.ShouldBe(retryable);
        result.Error.ShouldBe(success ? null : $"http-{status}");
    }

    [Fact]
    public async Task Discord_waits_for_ack_and_honors_fractional_retry_after()
    {
        Uri? destination = null;
        using var sender = new WebhookSender(new ResponseHandler(request => { destination = request.RequestUri; return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"retry_after\":1.25}") }; }));
        var result = await sender.SendAsync("https://discord.com/api/webhooks/123/secret?wait=false", "Discord", Payload, TestContext.Current.CancellationToken);
        destination!.Query.ShouldBe("?wait=true");
        result.RetryAfter.ShouldBe(TimeSpan.FromSeconds(1.25));
    }

    [Fact]
    public async Task Generic_json_has_versioned_summary_and_stable_event_header()
    {
        string? body = null, header = null;
        var payload = Payload;
        using var sender = new WebhookSender(new AsyncResponseHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            header = request.Headers.GetValues("X-TechStrap-Event-Id").Single();
            return new(HttpStatusCode.NoContent);
        }));
        (await sender.SendAsync("https://example.com/hook", "Json", payload, TestContext.Current.CancellationToken)).Success.ShouldBeTrue();
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("entity").GetProperty("title").GetString().ShouldBe(payload.Entity.Title);
        header.ShouldBe(payload.EventId.ToString());
        body!.ShouldNotContain("requester");
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class AsyncResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
