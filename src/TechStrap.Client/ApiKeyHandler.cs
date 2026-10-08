using Microsoft.Extensions.Options;
using TechStrap.Contracts.Http;

namespace TechStrap.Client;

/// <summary>
/// Adds the API key to every request, replacing any value the caller set. It refuses to send to an address other than the configured one, so a caller-built absolute URI can never
/// receive the key. The key is never written to an exception message.
/// </summary>
internal sealed class ApiKeyHandler(IOptions<TechStrapClientOptions> options) : DelegatingHandler
{
    /// <summary>Scheme, host and port (a default port is left out), never user info, so a message that names it cannot leak a credential embedded in an address.</summary>
    internal static string Authority(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var expected = Authority(settings.BaseAddress!);
        var actual = request.RequestUri is { IsAbsoluteUri: true } uri ? Authority(uri) : null;

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The request goes to {actual ?? "an unknown address"}, not to the configured TechStrap address {expected}. The API key is sent only to the configured address, so the request was not sent.");
        }

        request.Headers.Remove(HeaderNames.ApiKey);
        request.Headers.TryAddWithoutValidation(HeaderNames.ApiKey, settings.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
