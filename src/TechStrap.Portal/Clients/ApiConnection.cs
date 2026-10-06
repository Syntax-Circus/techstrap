using System.Net.Http.Json;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Http;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The only place the Portal talks HTTP to the API (D-045). It builds requests, sends them through the right named client (reads: retried; writes: never retried) and reads the answer into a
/// <see cref="Result"/>; the handler pipeline of each client forwards the visitor's address. A call made as a ticket's customer takes a <see cref="TicketToken"/>, which is set as the
/// <c>X-Ticket-Token</c> header of that one request: never in a URL, a body or a log (the clients have no logging handlers). Cancellation by the caller propagates as
/// <see cref="OperationCanceledException"/>; it is never turned into a Result. One instance per request scope.
/// </summary>
internal sealed class ApiConnection(IHttpClientFactory httpClients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One HttpClient per name for the life of the scope: the factory keeps every client it creates until the scope ends.
    private HttpClient? _read;
    private HttpClient? _write;

    private HttpClient ReadClient => _read ??= httpClients.CreateClient(ApiClientNames.Read);

    private HttpClient WriteClient => _write ??= httpClients.CreateClient(ApiClientNames.Write);

    /// <summary>GET through the retrying read client.</summary>
    public Task<Result<T>> GetAsync<T>(string uri, CancellationToken cancellationToken) => SendAsync<T>(ReadClient, new HttpRequestMessage(HttpMethod.Get, uri), null, cancellationToken);

    /// <summary>GET through the retrying read client as a ticket's customer. Every attempt carries the token.</summary>
    public Task<Result<T>> GetAsync<T>(string uri, TicketToken token, CancellationToken cancellationToken) => SendAsync<T>(ReadClient, new HttpRequestMessage(HttpMethod.Get, uri), token, cancellationToken);

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; the answer carries a JSON body.</summary>
    public Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, JsonRequest(method, uri, body), null, cancellationToken);

    /// <summary>The same as a ticket's customer: the token is the <c>X-Ticket-Token</c> header of this request.</summary>
    public Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, JsonRequest(method, uri, body), token, cancellationToken);

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; success has no body (202 or 204).</summary>
    public Task<Result> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken) =>
        SendAsync(WriteClient, JsonRequest(method, uri, body), null, cancellationToken);

    /// <summary>The same as a ticket's customer.</summary>
    public Task<Result> SendAsync(HttpMethod method, string uri, object? body, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync(WriteClient, JsonRequest(method, uri, body), token, cancellationToken);

    /// <summary>A write with a prepared body (a multipart ticket or reply). The content is used once: the caller builds a new one for every attempt.</summary>
    public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, new HttpRequestMessage(method, uri) { Content = content }, null, cancellationToken);

    /// <summary>The same as a ticket's customer.</summary>
    public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, new HttpRequestMessage(method, uri) { Content = content }, token, cancellationToken);

    /// <summary>
    /// GET through the retrying read client as a ticket's customer, returning as soon as the response headers have arrived (<see cref="HttpCompletionOption.ResponseHeadersRead"/>): the body is a live
    /// stream and is never buffered, so an attachment is copied to the visitor as it comes. On success the caller owns the <see cref="ApiDownload"/> and must dispose it. Every failure is a Result (the status
    /// decides, as for any call), and a cancellation by the caller propagates.
    /// </summary>
    public async Task<Result<ApiDownload>> OpenStreamAsync(string uri, TicketToken token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        Attach(request, token);
        HttpResponseMessage? response = null;
        var handedOver = false;
        try
        {
            response = await ReadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return Result<ApiDownload>.Failure(errors[0], [.. errors.Skip(1)]);
            }

            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            handedOver = true;
            return Result<ApiDownload>.Success(new ApiDownload(request, response, body));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return Result<ApiDownload>.Failure(ProblemMapping.Unexpected());
        }
        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
        {
            return Result<ApiDownload>.Failure(error);
        }
        finally
        {
            if (!handedOver)
            {
                response?.Dispose();
                request.Dispose();
            }
        }
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string uri, object? body) =>
        new(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };

    private static async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, TicketToken? token, CancellationToken cancellationToken)
    {
        using (request)
        {
            Attach(request, token);
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                    return Result<T>.Failure(errors[0], [.. errors.Skip(1)]);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                return value is null ? Result<T>.Failure(ProblemMapping.Unexpected()) : Result<T>.Success(value);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                // An answer that cannot be read (bad JSON, a content type or encoding the reader does not support).
                return Result<T>.Failure(ProblemMapping.Unexpected());
            }
            catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
            {
                return Result<T>.Failure(error);
            }
        }
    }

    private static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, TicketToken? token, CancellationToken cancellationToken)
    {
        using (request)
        {
            Attach(request, token);
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return Result.Success();
                }

                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return Result.Failure(errors[0], [.. errors.Skip(1)]);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return Result.Failure(ProblemMapping.Unexpected());
            }
            catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
            {
                return Result.Failure(error);
            }
        }
    }

    /// <summary>The one place a token becomes a header. It is set on the request, not on the client, so it cannot outlive the call or reach another one.</summary>
    private static void Attach(HttpRequestMessage request, TicketToken? token)
    {
        if (token is { } ticketToken)
        {
            request.Headers.Add(HeaderNames.TicketToken, ticketToken.Value);
        }
    }

    /// <summary>
    /// A transport failure (unreachable API, timeout) as a Result error. The message is fixed, user-safe copy, so it never includes exception text, a host or a port. Returns null for anything
    /// else, including a cancellation requested by the caller, which must keep propagating.
    /// </summary>
    internal static ResultError? Transport(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException or TimeoutException or HttpRequestException => ProblemMapping.Unavailable(),
        _ => null,
    };
}
