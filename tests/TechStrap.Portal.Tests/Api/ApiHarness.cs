using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// The Portal's API clients wired exactly as in Program (<c>AddPortalApiClients</c>, so the real forwarded-IP and resilience handlers run) with a <see cref="StubApiHandler"/> as the primary
/// handler and a request-like scope: an <c>HttpContext</c> whose connection address is the visitor's. Inject nothing but the types under test.
/// </summary>
internal sealed class ApiHarness : IDisposable
{
    public const string DefaultClientIp = "203.0.113.9";

    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private ApiHarness(ServiceProvider provider, StubApiHandler stub)
    {
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        Stub = stub;
    }

    public StubApiHandler Stub { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public static ApiHarness Create(string? clientIp = DefaultClientIp, TimeProvider? time = null, Action<IServiceCollection>? configure = null)
    {
        var stub = new StubApiHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null)
        {
            // The resilience pipelines take their clock from the container, so a test can step through the retry delays instead of waiting for them.
            services.AddSingleton(time);
        }

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new PortalOptions { ApiBaseUrl = "http://api.test/", PublicUrl = "https://portal.test" }));
        services.AddPortalApiClients();
        services.AddPortalSitemap();
        services.AddStubApi(stub);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        var harness = new ApiHarness(provider, stub);
        if (clientIp is not null)
        {
            harness.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Parse(clientIp) } };
        }

        return harness;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
