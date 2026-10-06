using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Api;

public static class PortalTestApi
{
    /// <summary>
    /// Makes <paramref name="stub"/> the primary handler of both named API clients. The handlers above it (forwarded client IP, and the read client's retries) are the real ones, so a test
    /// sees what the API would see.
    /// </summary>
    public static IServiceCollection AddStubApi(this IServiceCollection services, StubApiHandler stub)
    {
        // The tag handler is added last, so it is the innermost handler: it records which named client sent the request (a test pins each overload to its client).
        services.AddHttpClient(ApiClientNames.Read).AddHttpMessageHandler(() => new ClientTagHandler(ApiClientNames.Read)).ConfigurePrimaryHttpMessageHandler(_ => stub);
        services.AddHttpClient(ApiClientNames.Write).AddHttpMessageHandler(() => new ClientTagHandler(ApiClientNames.Write)).ConfigurePrimaryHttpMessageHandler(_ => stub);
        return services;
    }

    private sealed class ClientTagHandler(string name) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.Set(StubApiHandler.ClientKey, name);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Fails unless the Portal called the stub at least once and every call carried <c>X-Forwarded-For: {clientIp}</c>: a host test that makes an API call uses this, so a lost forwarded-IP
    /// handler fails the test (Review Focus 4: the API must rate-limit the visitor, not the Portal's container).
    /// </summary>
    public static void AssertEveryCallBore(this StubApiHandler stub, string clientIp)
    {
        var requests = stub.Requests;
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("The Portal made no API call, so there is no X-Forwarded-For header to check.");
        }

        var wrong = requests.FirstOrDefault(r => r.ForwardedFor != clientIp);
        if (wrong is not null)
        {
            throw new InvalidOperationException($"{wrong.Method} {wrong.Path} carried X-Forwarded-For '{wrong.ForwardedFor}', expected the visitor's address {clientIp}.");
        }
    }
}
