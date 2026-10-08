using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Client.Tests.Integration;

/// <summary>The real SDK registration pointed at a running Api host, with 1 ms retry delays.</summary>
internal sealed class SdkHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private SdkHost(ServiceProvider provider) => _provider = provider;

    public ITechStrapClient Client => _provider.GetRequiredService<ITechStrapClient>();

    /// <summary>Calls the in-process TestServer. <paramref name="below"/> is inserted closer to the transport than the client's own handlers.</summary>
    public static SdkHost ForTestServer(ClientApiFactory factory, string apiKey, DelegatingHandler? below = null)
    {
        var services = Register(factory.Server.BaseAddress, apiKey);
        var builder = services.AddHttpClient(TechStrapClientDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        if (below is not null)
        {
            builder.AddHttpMessageHandler(() => below);
        }

        return new SdkHost(services.BuildServiceProvider());
    }

    /// <summary>Calls the factory's real Kestrel socket (the factory must have been created with <c>UseKestrel</c>) through the default transport.</summary>
    public static SdkHost ForKestrel(ClientApiFactory factory, string apiKey)
    {
        using var probe = factory.CreateClient();
        return new SdkHost(Register(probe.BaseAddress!, apiKey).BuildServiceProvider());
    }

    private static ServiceCollection Register(Uri baseAddress, string apiKey)
    {
        var services = new ServiceCollection();
        services.AddTechStrapClient(options =>
        {
            options.BaseAddress = baseAddress;
            options.ApiKey = apiKey;
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
        });
        return services;
    }

    public void Dispose() => _provider.Dispose();
}
