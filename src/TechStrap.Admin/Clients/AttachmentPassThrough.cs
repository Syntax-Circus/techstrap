using System.Net;
using Microsoft.Net.Http.Headers;

namespace TechStrap.Admin.Clients;

/// <summary>
/// <c>GET /attachments/{id}</c>: the exempt pass-through adapter of D-017. A browser link cannot carry the agent's bearer token, so the Admin streams
/// <c>GET api/attachments/{id}</c> for the signed-in agent. It runs no workflow and touches no data. It never buffers the file, and it forces a download
/// (<c>Content-Disposition: attachment</c>), <c>nosniff</c> and no caching, whatever the API sent. An upstream 404 is a 404; the API alone decides who may read a file.
/// This is a request-scoped call (HttpContext present), so the auth handler resolves the token from the cookie, not from a circuit.
/// </summary>
public static class AttachmentPassThrough
{
    public const string Route = "/attachments/{id:guid}";

    /// <summary>The path every download is under. The host adds <c>sandbox</c> to the Content-Security-Policy of these responses (the shared security headers would overwrite a value set here).</summary>
    public const string Prefix = "/attachments";

    public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, StreamAsync);
        return endpoints;
    }

    private static async Task StreamAsync(Guid id, HttpContext http, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(ApiClientNames.Read);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/attachments/{id}");
        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            http.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstream)
        {
            if (!upstream.IsSuccessStatusCode)
            {
                http.Response.StatusCode = upstream.StatusCode switch
                {
                    HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status502BadGateway,
                };
                return;
            }

            var response = http.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            if (upstream.Content.Headers.ContentLength is { } length)
            {
                response.ContentLength = length;
            }

            response.Headers[HeaderNames.ContentDisposition] = AttachmentDisposition(upstream.Content.Headers.ContentDisposition);
            response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            response.Headers[HeaderNames.CacheControl] = "private, no-store";
            response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox";

            await using var body = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            await body.CopyToAsync(response.Body, cancellationToken);
        }
    }

    /// <summary>The upstream header with its file name, but always of type "attachment" (never "inline").</summary>
    internal static string AttachmentDisposition(System.Net.Http.Headers.ContentDispositionHeaderValue? upstream)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        if (upstream?.FileNameStar is { Length: > 0 } fileNameStar)
        {
            disposition.SetHttpFileName(fileNameStar);
        }
        else if (upstream?.FileName is { Length: > 0 } fileName)
        {
            disposition.SetHttpFileName(fileName.Trim('"'));
        }

        return disposition.ToString();
    }
}
