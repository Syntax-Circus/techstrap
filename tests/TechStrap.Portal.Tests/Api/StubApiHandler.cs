using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TechStrap.Contracts.Http;

namespace TechStrap.Portal.Tests.Api;

/// <summary>One request the Portal sent to the stub API.</summary>
public sealed record StubApiRequest(HttpMethod Method, string Path, string Query, string? ForwardedFor, string? TicketToken, string? ContentType, string? Body, string? Client = null);

/// <summary>
/// Stands in for the TechStrap API behind the Portal's named HTTP clients (it replaces their primary handler, so the real handler pipeline above it still runs: the forwarded-IP handler
/// adds <c>X-Forwarded-For</c>, the read client's retry handler retries). It records every request, with the two headers the Portal is responsible for, and answers by method and path; an
/// unconfigured request answers 404 with the problem code "stub-not-configured" so a test fails loudly. The Portal's own copy: the Admin's stub speaks the Admin's DTOs and bearer token.
/// </summary>
public sealed class StubApiHandler : HttpMessageHandler
{
    /// <summary>Set on every request by the tag handler of each named client, so a test can see which client sent it.</summary>
    public static readonly HttpRequestOptionsKey<string> ClientKey = new("stub-client");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<StubApiRequest> _requests = [];
    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, HttpResponseMessage> Respond)> _routes = [];
    private readonly object _gate = new();

    /// <summary>Every request received so far, in order.</summary>
    public IReadOnlyList<StubApiRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>How many requests with this method and path (any query) were received.</summary>
    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Answers requests for <paramref name="path"/> (path only, no query) with whatever <paramref name="respond"/> returns. A later route for the same method and path replaces an earlier one.</summary>
    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            _routes.RemoveAll(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _routes.Add((method, path, respond));
        }

        return this;
    }

    public StubApiHandler OnJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK) => On(method, path, _ => JsonResponse(status, body));

    public StubApiHandler OnStatus(HttpMethod method, string path, HttpStatusCode status) => On(method, path, _ => new HttpResponseMessage(status));

    /// <summary>An RFC 7807 answer in the shape the API produces: <c>type</c> is the error code, <c>detail</c> the message.</summary>
    public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail) => On(method, path, _ => Problem(status, type, detail));

    /// <summary>A file download as the API sends it: the body as a stream (so a test can see whether it was read), a content type and, when given, a <c>Content-Disposition</c> with the stored name.</summary>
    public StubApiHandler OnFile(HttpMethod method, string path, byte[] bytes, string contentType, string? fileName = null) =>
        On(method, path, _ => FileResponse(new MemoryStream(bytes), contentType, fileName));

    public static HttpResponseMessage FileResponse(Stream body, string contentType, string? fileName = null)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        if (body.CanSeek)
        {
            content.Headers.ContentLength = body.Length;
        }

        if (fileName is not null)
        {
            content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = $"\"{fileName}\"" };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body, options: Json) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { type, title = status.ToString(), status = (int)status, detail }, Json),
            Encoding.UTF8,
            "application/problem+json"),
    };

    /// <summary>
    /// A 400 in the shape the API produces for a validation failure: <c>type</c> "validation-failed", the message per field in <c>errors</c> and the specific code per field in
    /// <c>errorCodes</c>. <paramref name="target"/> is the field exactly as the API sends it, kebab-case; an empty target is a form-level error.
    /// </summary>
    public static HttpResponseMessage ValidationProblem(string target, string code, string message) => ValidationProblem([(target, code, message)]);

    public static HttpResponseMessage ValidationProblem(IReadOnlyList<(string Target, string Code, string Message)> errors)
    {
        var messages = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        var codes = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray());
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { type = "validation-failed", title = "One or more validation errors occurred.", status = 400, detail = errors[0].Message, errors = messages, errorCodes = codes },
                    Json),
                Encoding.UTF8,
                "application/problem+json"),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real handler gives up on a cancelled token before it sends anything.
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new StubApiRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            Header(request, "X-Forwarded-For"),
            Header(request, HeaderNames.TicketToken),
            request.Content?.Headers.ContentType?.ToString(),
            body,
            request.Options.TryGetValue(ClientKey, out var client) ? client : null);

        Func<StubApiRequest, HttpResponseMessage>? respond;
        lock (_gate)
        {
            _requests.Add(seen);
            respond = _routes.FirstOrDefault(r => r.Method == seen.Method && string.Equals(r.Path, seen.Path, StringComparison.OrdinalIgnoreCase)).Respond;
        }

        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : respond(seen);
    }

    private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
