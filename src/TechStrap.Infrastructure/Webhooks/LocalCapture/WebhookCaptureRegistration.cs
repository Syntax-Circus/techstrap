#if DEBUG
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TechStrap.Infrastructure.Webhooks.LocalCapture;

/// <summary>
/// Local development only: sends every webhook delivery to a catcher on this machine instead of its real destination. The whole folder is left out of
/// builds that are not Debug. Only the URL checks of WebhookPolicy.ValidUrl still run; host names are not resolved, so the sender's
/// public-address check does not apply.
/// </summary>
public static class WebhookCaptureRegistration
{
    public const string ConfigurationKey = "TECHSTRAP_WEBHOOK_CAPTURE_URL";

    /// <summary>Carries the host of the real destination only: webhook paths and queries contain credentials.</summary>
    public const string OriginalHostHeader = "X-TechStrap-Capture-Original-Host";

    /// <summary>Registers the singleton <see cref="IWebhookSender"/>. The setting is read when the sender is first resolved, so late configuration sources count.</summary>
    public static IServiceCollection AddTechStrapWebhookSender(this IServiceCollection services) =>
        services.AddSingleton<IWebhookSender>(provider => CreateSender(
            provider.GetRequiredService<IConfiguration>()[ConfigurationKey],
            provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WebhookCaptureRegistration).FullName!)));

    internal static WebhookSender CreateSender(string? captureUrl, bool isDevelopment, ILogger logger, HttpMessageHandler? transport = null)
    {
        if (string.IsNullOrWhiteSpace(captureUrl))
        {
            return new WebhookSender();
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException($"{ConfigurationKey} works only in the Development environment.");
        }

        // The value is never echoed: a mistake here could be a pasted real webhook URL, which is a credential.
        if (!Uri.TryCreate(captureUrl, UriKind.Absolute, out var target) || !IsLocalCatcher(target))
        {
            throw new InvalidOperationException($"{ConfigurationKey} must be an http URL on localhost or a loopback address, without user info, query or fragment.");
        }

        logger.LogWarning("Webhook capture is on: every webhook delivery goes to the local catcher set in {ConfigurationKey}.", ConfigurationKey);
        return new WebhookSender(new CaptureHandler(target, transport ?? CreateTransport()));
    }

    private static bool IsLocalCatcher(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address)));

    private static SocketsHttpHandler CreateTransport() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        // As in WebhookSender: no HTTP activities or breadcrumbs for webhook traffic.
        ActivityHeadersPropagator = null,
    };

    private sealed class CaptureHandler(Uri target, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var originalHost = request.RequestUri!.IdnHost;
            request.RequestUri = target;
            request.Headers.Remove(OriginalHostHeader);
            request.Headers.Add(OriginalHostHeader, originalHost);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
#endif
