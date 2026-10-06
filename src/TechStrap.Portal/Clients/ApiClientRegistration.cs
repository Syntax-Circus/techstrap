using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Clients;

public static class ApiClientRegistration
{
    /// <summary>Retries after the first attempt: a read is at most 1 + this many calls.</summary>
    public const int ReadRetryCount = 2;

    /// <summary>How long a read may take in all, retries included.</summary>
    public const int ReadTimeoutSeconds = 30;

    /// <summary>A write may carry a multipart ticket or reply with attachments (up to 25 MB), so it gets a longer deadline.</summary>
    public const int WriteTimeoutSeconds = 300;

    /// <summary>
    /// The longest a read waits before a retry, whatever the API's <c>Retry-After</c> asks for. The resilience default honours the header with no limit, so an overloaded API that says "120"
    /// would freeze a page on "loading" until the client timeout. The header is still honoured below this cap.
    /// </summary>
    public static readonly TimeSpan ReadRetryAfterCap = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ReadRetryBaseDelay = TimeSpan.FromMilliseconds(250);

    // Refresh pooled connections so a DNS change of the API is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two named API clients and the typed clients (D-045). Both named clients forward the visitor's address; only <see cref="ApiClientNames.Read"/> retries. Components inject the
    /// <c>I*Client</c> interfaces, never an HttpClient. The base address comes from the validated <see cref="PortalOptions"/> when a client is first created.
    /// </summary>
    public static IServiceCollection AddPortalApiClients(this IServiceCollection services)
    {
        // Retry only: no circuit breaker. The read client is shared by every visitor and every read, so a breaker opened by one failing endpoint would lock every other visitor out.
        // No logging handlers: the default HttpClient logging writes every request header at Trace, and a customer call carries X-Ticket-Token (the leak tests scan every level).
        services.AddHttpClient(ApiClientNames.Read)
            .RemoveAllLoggers()
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime })
            .AddResilienceHandler("techstrap-portal-api-read-retry", builder => builder.AddRetry(ReadRetry()));

        services.AddHttpClient(ApiClientNames.Write)
            .RemoveAllLoggers()
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddScoped<ApiConnection>();
        services.AddScoped<IPublicProductClient, PublicProductClient>();
        return services;
    }

    // Transport errors, timeouts, 408 and 502/503/504. A 500 is the API's own answer to this request and is not retried.
    private static HttpRetryStrategyOptions ReadRetry() => new()
    {
        MaxRetryAttempts = ReadRetryCount,
        Delay = ReadRetryBaseDelay,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,

        // The default honours Retry-After without a limit; this one honours it up to ReadRetryAfterCap and otherwise falls back to the backoff above.
        ShouldRetryAfterHeader = false,
        DelayGenerator = args => ValueTask.FromResult(RetryAfterDelay(args.Outcome.Result, TimeProvider.System.GetUtcNow())),
        ShouldHandle = args => ValueTask.FromResult(IsRetryable(args)),
    };

    /// <summary>
    /// Defence in depth: the read client is only ever given GETs by <see cref="ApiConnection"/>, but a write must never be retried even if one is sent through it by mistake. The request is taken
    /// from the response when there is one, and from the resilience context otherwise (an exception has no response), so a transport failure of a non-GET is not retried either. A request that
    /// cannot be found is not retried.
    /// </summary>
    internal static bool IsRetryable(RetryPredicateArguments<HttpResponseMessage> args)
    {
        var method = args.Outcome.Result?.RequestMessage?.Method ?? args.Context.GetRequestMessage()?.Method;
        if (method != HttpMethod.Get)
        {
            return false;
        }

        return args.Outcome switch
        {
            { Exception: HttpRequestException or TimeoutException } => true,
            { Exception: OperationCanceledException } => !args.Context.CancellationToken.IsCancellationRequested,
            { Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
            _ => false,
        };
    }

    /// <summary>
    /// The wait the API asked for in <c>Retry-After</c> (seconds or an HTTP date), capped at <see cref="ReadRetryAfterCap"/>; null when there is no usable header, so the exponential backoff
    /// applies. A date in the past asks for no wait beyond the backoff.
    /// </summary>
    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage? response, DateTimeOffset now)
    {
        var retryAfter = response?.Headers.RetryAfter;
        TimeSpan? requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        if (requested is not { } wait || wait <= TimeSpan.Zero)
        {
            return null;
        }

        return wait < ReadRetryAfterCap ? wait : ReadRetryAfterCap;
    }

    private static void ConfigureClient(IServiceProvider services, HttpClient client, int timeoutSeconds)
    {
        client.BaseAddress = services.GetRequiredService<IOptions<PortalOptions>>().Value.ApiBaseUri;
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }
}
