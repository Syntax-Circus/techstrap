using System.Net;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 4 end to end: behind a trusted reverse proxy the Portal sees the visitor's address (from the proxy's <c>X-Forwarded-For</c>), and the API sees the same address from the Portal,
/// not the Portal's container and not the proxy (D-019). An untrusted peer's header is ignored.
/// </summary>
public sealed class ForwardedClientIpHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void ConfigureProduct(PortalFactory factory) =>
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));

    [Fact]
    public async Task The_visitors_address_from_a_trusted_proxy_reaches_the_api()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("192.0.2.10"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("ok");
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Fact]
    public async Task The_last_untrusted_address_in_a_forwarded_chain_is_the_visitor()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("192.0.2.10"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "198.51.100.200, 203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Fact]
    public async Task A_forwarded_header_from_an_untrusted_peer_is_ignored_and_the_peer_is_the_visitor()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("198.51.100.50"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("198.51.100.50");
    }
}
