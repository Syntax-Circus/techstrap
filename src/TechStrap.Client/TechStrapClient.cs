using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using SyntaxCircus.Http.Resilience;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client;

/// <summary>
/// The submit call over one <see cref="HttpRequestResiliencePipeline"/>, which is shared for the life of the process so its circuit breaker is too. Facts of the pipeline (SyntaxCircus.Http.Resilience 0.2.2, D-047):
/// the final failing response is returned, not thrown (this class disposes it); once the attempts are spent the original <see cref="HttpRequestException"/> is rethrown; a not-replayable call sends exactly once;
/// the total timeout covers the sends and the waits between them and surfaces as <see cref="HttpRequestTimeoutException"/>; the circuit counts calls, not attempts, and answers <see cref="HttpCircuitOpenException"/>
/// without calling the handler. Nothing here formats the API key or the request body into a message.
/// </summary>
internal sealed class TechStrapClient(IHttpClientFactory httpClients, IOptions<TechStrapClientOptions> options, TimeProvider timeProvider) : ITechStrapClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private HttpRequestResiliencePipeline? _pipeline;

    public Task<Result<SubmitTicketResponse>> SubmitTicketAsync(SubmitTicketRequest request, string? idempotencyKey = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = idempotencyKey is null ? Guid.NewGuid().ToString("N") : ValidateKey(idempotencyKey);
        return SendAsync(request, key, HttpRequestReplaySafety.Replayable, ct);
    }

    public Task<Result<SubmitTicketResponse>> SubmitTicketOnceAsync(SubmitTicketRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(request, null, HttpRequestReplaySafety.NotReplayable, ct);
    }

    private static string ValidateKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("The idempotency key must not be blank.", nameof(idempotencyKey));
        }

        if (idempotencyKey.Length > IntakeLimits.MaxIdempotencyKeyLength)
        {
            throw new ArgumentException($"The idempotency key must be at most {IntakeLimits.MaxIdempotencyKeyLength} characters.", nameof(idempotencyKey));
        }

        if (idempotencyKey.Any(c => c is < '!' or > '~'))
        {
            throw new ArgumentException("The idempotency key must use visible ASCII characters only.", nameof(idempotencyKey));
        }

        return idempotencyKey;
    }

    /// <summary>A new message and new content for every attempt: the pipeline disposes both after each send.</summary>
    private static HttpRequestMessage BuildRequest(SubmitTicketRequest request, string? key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, IntakeRoutes.Tickets) { Content = JsonContent.Create(request, options: _json) };
        if (key is not null)
        {
            message.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, key);
        }

        return message;
    }

    private static bool IsUnavailable(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => false,
        OperationCanceledException or TimeoutException or HttpRequestException => true,
        _ => false,
    };

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private async Task<Result<SubmitTicketResponse>> SendAsync(SubmitTicketRequest request, string? key, HttpRequestReplaySafety safety, CancellationToken ct)
    {
        var pipeline = Pipeline();
        using var client = httpClients.CreateClient(TechStrapClientDefaults.HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await pipeline.SendAsync(
                (_, _) => ValueTask.FromResult(BuildRequest(request, key)),
                (message, completion, token) => client.SendAsync(message, completion, token),
                HttpCompletionOption.ResponseContentRead,
                safety,
                cancellationToken: ct);
        }
        catch (Exception ex) when (IsUnavailable(ex, ct))
        {
            // A caller who cancelled wins over whatever the pipeline surfaced.
            ct.ThrowIfCancellationRequested();
            return Result<SubmitTicketResponse>.Failure(ProblemResponseMapper.Unavailable());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errors = ProblemResponseMapper.Map(response.StatusCode, await ReadBodyAsync(response, ct));
                return Result<SubmitTicketResponse>.Failure(errors[0], [.. errors.Skip(1)]);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(_json, ct);
                return value is null || string.IsNullOrWhiteSpace(value.TicketNumber) ? Result<SubmitTicketResponse>.Failure(ProblemResponseMapper.Unexpected()) : Result<SubmitTicketResponse>.Success(value);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                // An answer that cannot be read (bad JSON, a content type or encoding the reader does not support).
                return Result<SubmitTicketResponse>.Failure(ProblemResponseMapper.Unexpected());
            }
        }
    }

    /// <summary>Built on first use, so invalid options surface as an <see cref="OptionsValidationException"/> from the first call and a failed build is retried by the next.</summary>
    private HttpRequestResiliencePipeline Pipeline()
    {
        if (Volatile.Read(ref _pipeline) is { } existing)
        {
            return existing;
        }

        lock (_gate)
        {
            if (_pipeline is not null)
            {
                return _pipeline;
            }

            var settings = options.Value;
            var created = new HttpRequestResiliencePipeline(
                ResilienceDefaults.PipelineName,
                new HttpRequestResilienceOptions
                {
                    MaxAttempts = settings.MaxAttempts,
                    TotalRequestTimeout = settings.Timeout,
                    BackoffBaseDelay = settings.RetryBaseDelay,
                    MaximumDelay = settings.MaxRetryDelay,
                    RetryableStatusCodes = new HashSet<HttpStatusCode>
                    {
                        HttpStatusCode.RequestTimeout,
                        HttpStatusCode.BadGateway,
                        HttpStatusCode.ServiceUnavailable,
                        HttpStatusCode.GatewayTimeout,
                    },
                    RetryableExceptionCategories = new HashSet<HttpResilienceFailureCategory>
                    {
                        HttpResilienceFailureCategory.Transport,
                        HttpResilienceFailureCategory.Timeout,
                    },
                    CircuitFailureRatio = ResilienceDefaults.CircuitFailureRatio,
                    CircuitMinimumThroughput = ResilienceDefaults.CircuitMinimumThroughput,
                    CircuitSamplingDuration = ResilienceDefaults.CircuitSamplingDuration,
                    CircuitBreakDuration = ResilienceDefaults.CircuitBreakDuration,
                    TimeProvider = timeProvider,
                });
            Volatile.Write(ref _pipeline, created);
            return created;
        }
    }
}
