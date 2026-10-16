using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Infrastructure.Webhooks;

public sealed record WebhookSendResult(bool Success, bool Retryable, string? Error, TimeSpan? RetryAfter = null);
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(string url, string format, WebhookEventDto payload, CancellationToken cancellationToken);
}

/// <summary>Dedicated client without factory logging; DNS answers are validated and the validated IP is connected directly.</summary>
public sealed class WebhookSender : IWebhookSender, IDisposable
{
    private const int TimeoutSeconds = 10;
    private readonly HttpClient _client;

    public WebhookSender() : this(CreateHandler()) { }
    internal WebhookSender(HttpMessageHandler handler) => _client = new(handler) { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        // In .NET 10 a null propagator also omits DiagnosticsHandler from this client's pipeline.
        // Webhook paths/queries contain credentials and must never be exported in HTTP activities or breadcrumbs.
        ActivityHeadersPropagator = null,
        ConnectCallback = ConnectAsync, PooledConnectionLifetime = TimeSpan.FromMinutes(1),
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext connection, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(connection.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(x => !WebhookPolicy.IsPublicAddress(x))) throw new HttpRequestException("webhook-destination-blocked");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, connection.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("webhook-connect-failed");
    }

    public async Task<WebhookSendResult> SendAsync(string url, string format, WebhookEventDto payload, CancellationToken cancellationToken)
    {
        if (!WebhookPolicy.ValidUrl(url)) return new(false, false, "destination-invalid");
        try
        {
            // wait=true makes Discord acknowledge message creation, rather than an unconfirmed enqueue.
            if (format == "Discord")
            {
                var builder = new UriBuilder(url);
                var query = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith("wait=", StringComparison.OrdinalIgnoreCase));
                builder.Query = string.Join('&', query.Append("wait=true"));
                url = builder.Uri.AbsoluteUri;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = format == "Discord" ? JsonContent.Create(DiscordPayload(payload)) : JsonContent.Create(payload, options: WebhookJson.Options),
            };
            request.Headers.Add("X-TechStrap-Event-Id", payload.EventId.ToString());
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return new(true, false, null);
            var code = (int)response.StatusCode;
            TimeSpan? retry = response.Headers.RetryAfter?.Delta;
            if (response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
            if (code == 429 && format == "Discord")
            {
                using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bodyTimeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                await using var stream = await response.Content.ReadAsStreamAsync(bodyTimeout.Token);
                var buffer = new byte[4096];
                var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, bodyTimeout.Token);
                try
                {
                    using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
                    if (json.RootElement.TryGetProperty("retry_after", out var delay) && delay.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.FromDays(1).TotalSeconds)
                        retry = TimeSpan.FromSeconds(seconds);
                }
                catch (JsonException) { /* Fall back to Retry-After or the normal backoff. */ }
            }
            return new(false, code is 408 or 429 || code >= 500, $"http-{code}", retry);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, true, "timeout"); }
        catch (HttpRequestException) { return new(false, true, "transport-failed"); }
        catch (IOException) { return new(false, true, "transport-failed"); }
    }

    internal static object DiscordPayload(WebhookEventDto payload) => new
    {
        allowed_mentions = new { parse = Array.Empty<string>() },
        embeds = new[] { new
        {
            title = Clip($"{payload.Product.Name}: {payload.Entity.Number ?? payload.Entity.Type} — {payload.Entity.Title}", 256),
            url = payload.Entity.AdminUrl,
            description = Clip($"{payload.EventType}\nStatus: {payload.Entity.Status}" + (payload.Entity.PreviousStatus is null ? "" : $" (was {payload.Entity.PreviousStatus})") + (payload.Entity.TicketType is null ? "" : $"\nType: {payload.Entity.TicketType}"), 4096),
            timestamp = payload.OccurredAt,
            footer = new { text = $"TechStrap • {payload.EventId}" },
        } },
    };

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
    public void Dispose() => _client.Dispose();
}
