using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// The Admin's API clients wired exactly as in Program (<c>AddBlazorTokenForwarding</c> + <c>AddTechStrapApiClients</c>, so the real auth, forwarded-IP and
/// resilience handlers run) with a <see cref="StubApiHandler"/> as the primary handler and a circuit-like scope: no HttpContext, a fixed authentication state, the
/// principal's access token in the server token cache. Inject nothing but the interfaces under test.
/// </summary>
internal sealed class ApiHarness : IAsyncDisposable
{
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

    public static async Task<ApiHarness> CreateAsync(AdminTestPrincipal? principal = null, bool seedToken = true, TimeProvider? time = null)
    {
        principal ??= AdminTestPrincipal.Agent;
        var stub = new StubApiHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(AdminTestSettings.With()).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null)
        {
            // The resilience pipelines take their clock from the container, so a test can step through the retry delays instead of waiting for them.
            services.AddSingleton(time);
        }

        services.AddSingleton<IConfiguration>(configuration);
        services.AddBlazorTokenForwarding(configuration, "Auth");
        services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(principal.ToClaimsPrincipal("Test")));
        services.AddTechStrapApiClients();
        services.AddStubApi(stub);

        var provider = services.BuildServiceProvider();
        if (seedToken)
        {
            await provider.GetRequiredService<IServerTokenCache>().SetAsync(
                provider.GetRequiredService<IUserTokenCacheKeyProvider>().GetCacheKey(principal.Subject)!,
                new ServerTokenCacheEntry(principal.AccessToken, null, null, DateTimeOffset.UtcNow.AddDays(1)));
        }

        return new ApiHarness(provider, stub);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
