using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Http.Resilience;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Clients;

public static class ApiClientRegistration
{
    public const int ReadRetryCount = 3;

    // A circuit can live for hours: refresh pooled connections so a DNS change of the API is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two named API clients and the scoped typed clients (D-040). Both named clients run the auth handler (bearer token) and forward the
    /// caller's IP; only <see cref="ApiClientNames.Read"/> retries. Components inject the <c>I*Client</c> interfaces, never an HttpClient.
    /// The base address and timeout come from the validated <c>Api</c> options when a client is first created.
    /// </summary>
    public static IServiceCollection AddTechStrapApiClients(this IServiceCollection services)
    {
        services.AddResilientHttpClient(ApiClientNames.Read, retryCount: ReadRetryCount)
            .ConfigureHttpClient(ConfigureClient)
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddHttpClient(ApiClientNames.Write)
            .ConfigureHttpClient(ConfigureClient)
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

    private static void ConfigureClient(IServiceProvider services, HttpClient client)
    {
        var api = services.GetRequiredService<IOptions<ApiOptions>>().Value;
        client.BaseAddress = new Uri(api.BaseUrl.EndsWith('/') ? api.BaseUrl : api.BaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(api.TimeoutSeconds);
    }
}
