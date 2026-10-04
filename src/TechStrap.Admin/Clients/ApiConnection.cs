using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Common;

namespace TechStrap.Admin.Clients;

/// <summary>
/// The only place the Admin talks HTTP to the API (D-040). It builds requests, sends them through the right named client (reads: retried; writes: never
/// retried) obtained from <see cref="IBlazorCircuitHttpClientFactory"/> so the circuit's token reaches the auth handler, and reads the answer into a
/// <see cref="Result"/>. It does not use ApiClientBase because that drops the <c>errorCodes</c> the API sends and has no Result mapping. Cancellation by the
/// caller propagates as <see cref="OperationCanceledException"/>; it is never turned into a Result. One instance per scope (circuit).
/// </summary>
internal sealed class ApiConnection(IBlazorCircuitHttpClientFactory httpClients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One HttpClient per name for the life of the scope: the factory keeps every client it creates until the scope ends.
    private HttpClient? _read;
    private HttpClient? _write;

    private HttpClient ReadClient => _read ??= httpClients.CreateClient(ApiClientNames.Read);

    private HttpClient WriteClient => _write ??= httpClients.CreateClient(ApiClientNames.Write);

    /// <summary>GET through the retrying read client.</summary>
    public async Task<Result<T>> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync<T>(ReadClient, request, cancellationToken);
    }

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; the answer carries a JSON body.</summary>
    public async Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };
        return await SendAsync<T>(WriteClient, request, cancellationToken);
    }

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; success has no body (204).</summary>
    public async Task<Result> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };
        return await SendAsync(WriteClient, request, cancellationToken);
    }

    /// <summary>A write with a prepared body (the multipart reply). The content is used once: the caller builds a new one for every attempt.</summary>
    public async Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        return await SendAsync<T>(WriteClient, request, cancellationToken);
    }

    private static async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return Result<T>.Failure(errors[0], [.. errors.Skip(1)]);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            return value is null ? Result<T>.Failure(Unexpected()) : Result<T>.Success(value);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            // An answer that cannot be read (bad JSON, a content type or encoding the reader does not support).
            return Result<T>.Failure(Unexpected());
        }
        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
        {
            return Result<T>.Failure(error);
        }
    }

    private static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
            return Result.Failure(Unexpected());
        }
        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
        {
            return Result.Failure(error);
        }
    }

    /// <summary>
    /// A transport failure (unreachable API, timeout) as a Result error. The message is fixed, user-safe copy: the gate and the pages print it as is,
    /// so it never includes exception text, a host or a port. Returns null for anything else, including a cancellation requested by the caller,
    /// which must keep propagating.
    /// </summary>
    internal static ResultError? Transport(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException or TimeoutException => new ResultError(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again.", ResultErrorKind.Failure),
        HttpRequestException => new ResultError(ApiErrorCodes.ApiUnavailable, "TechStrap could not reach the API. Try again in a moment.", ResultErrorKind.Failure),
        _ => null,
    };

    private static ResultError Unexpected() =>
        new(ApiErrorCodes.UnexpectedResponse, "The API answered in a form TechStrap did not expect. Tell an admin if it keeps happening.", ResultErrorKind.Failure);
}

/// <summary>Builds a request URI with a query string, skipping null and empty values. Values are formatted invariantly and escaped.</summary>
internal static class ApiUri
{
    public static string Build(string path, params (string Key, object? Value)[] query)
    {
        var parts = query
            .Where(q => q.Value is not null && Format(q.Value).Length > 0)
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(Format(q.Value!))}")
            .ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private static string Format(object value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
}
