namespace TechStrap.Portal.Clients;

/// <summary>
/// A file the API is sending, opened as soon as its response headers arrived: <see cref="Body"/> is the live stream (nothing is buffered). The caller copies it to the visitor and must dispose the download, which
/// closes the upstream response. Only the headers the pass-through needs are exposed; the API's own <c>Content-Disposition</c> type is never passed on (the pass-through always forces a download).
/// </summary>
public sealed class ApiDownload : IAsyncDisposable
{
    public const string FallbackContentType = "application/octet-stream";

    private readonly HttpRequestMessage _request;
    private readonly HttpResponseMessage _response;

    internal ApiDownload(HttpRequestMessage request, HttpResponseMessage response, Stream body)
    {
        _request = request;
        _response = response;
        Body = body;
    }

    public Stream Body { get; }

    public string ContentType => _response.Content.Headers.ContentType?.ToString() ?? FallbackContentType;

    public long? ContentLength => _response.Content.Headers.ContentLength;

    /// <summary>The file name the API stored, from <c>Content-Disposition</c>; null when it sent none.</summary>
    public string? FileName
    {
        get
        {
            var disposition = _response.Content.Headers.ContentDisposition;
            return disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Body.DisposeAsync();
        }
        finally
        {
            // Whatever closing the body did, the response and the request are released, so the connection goes back to the pool.
            _response.Dispose();
            _request.Dispose();
        }
    }
}
