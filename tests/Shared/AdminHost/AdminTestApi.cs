using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;

namespace TechStrap.Tests.Shared.AdminHost;

public static class AdminTestApi
{
    /// <summary>
    /// Makes <paramref name="stub"/> the primary handler of both named API clients. The handlers above it (bearer token, forwarded IP, and the read client's
    /// retries) are the real ones, so a test sees what the API would see.
    /// </summary>
    public static IServiceCollection AddStubApi(this IServiceCollection services, StubApiHandler stub)
    {
        services.AddHttpClient(ApiClientNames.Read).ConfigurePrimaryHttpMessageHandler(_ => stub);
        services.AddHttpClient(ApiClientNames.Write).ConfigurePrimaryHttpMessageHandler(_ => stub);
        return services;
    }

    /// <summary>
    /// Fails unless the Admin called the stub at least once and every call carried <c>Bearer {principal's access token}</c>: a host test that makes an API call
    /// through a signed-in host uses this, so a missing auth handler fails the test (Review Focus 2).
    /// </summary>
    public static void AssertEveryCallBore(this StubApiHandler stub, AdminTestPrincipal principal)
    {
        var requests = stub.Requests;
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("The Admin made no API call, so there is no Authorization header to check.");
        }

        var expected = "Bearer " + principal.AccessToken;
        var wrong = requests.FirstOrDefault(r => r.Authorization != expected);
        if (wrong is not null)
        {
            throw new InvalidOperationException($"{wrong.Method} {wrong.Path} carried the Authorization header '{wrong.Authorization}', expected the bearer token of {principal.Name}.");
        }
    }
}
