using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Clients;

public static class ApiClientRegistration
{
    /// <summary>Retries after the first attempt: a read is at most 1 + this many calls.</summary>
    public const int ReadRetryCount = 2;

    /// <summary>The write client allows a multipart reply with attachments at least this long, whatever the configured read timeout.</summary>
    public const int WriteTimeoutFloorSeconds = 300;

    private static readonly TimeSpan ReadRetryBaseDelay = TimeSpan.FromMilliseconds(250);

    // A circuit can live for hours: refresh pooled connections so a DNS change of the API is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two named API clients and the scoped typed clients (D-040). Both named clients run the auth handler (bearer token) and forward the
    /// caller's IP; only <see cref="ApiClientNames.Read"/> retries. Components inject the <c>I*Client</c> interfaces, never an HttpClient.
    /// The base address and timeout come from the validated <c>Api</c> options when a client is first created.
    /// </summary>
    public static IServiceCollection AddTechStrapApiClients(this IServiceCollection services)
    {
        // Retry only: no circuit breaker. The read client is a singleton per name shared by every agent and every read, so a breaker opened by one
        // failing endpoint would lock every other agent out of /api/agents/me and the pass-through.
        services.AddHttpClient(ApiClientNames.Read)
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, minimumTimeoutSeconds: 0))
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime })
            .AddResilienceHandler("techstrap-api-read-retry", builder => builder.AddRetry(ReadRetry()));

        services.AddHttpClient(ApiClientNames.Write)
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutFloorSeconds))
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddScoped<ApiConnection>();
        services.AddScoped<IAgentsClient, AgentsClient>();
        services.AddScoped<IProductsClient, ProductsClient>();
        services.AddScoped<ITagsClient, TagsClient>();
        services.AddScoped<ITicketsClient, TicketsClient>();
        services.AddScoped<IRequestersClient, RequestersClient>();
        services.AddScoped<AgentSession>();
        return services;
    }

    // Transport errors, timeouts, 408 and 502/503/504. A 500 is the API's own answer to this request and is not retried.
    private static HttpRetryStrategyOptions ReadRetry() => new()
    {
        MaxRetryAttempts = ReadRetryCount,
        Delay = ReadRetryBaseDelay,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
        {
            { Exception: HttpRequestException or TimeoutException } => true,
            { Exception: OperationCanceledException } => !args.Context.CancellationToken.IsCancellationRequested,
            { Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
            _ => false,
        }),
    };

    private static void ConfigureClient(IServiceProvider services, HttpClient client, int minimumTimeoutSeconds)
    {
        var api = services.GetRequiredService<IOptions<ApiOptions>>().Value;
        client.BaseAddress = new Uri(api.BaseUrl.EndsWith('/') ? api.BaseUrl : api.BaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(api.TimeoutSeconds, minimumTimeoutSeconds));
    }
}
