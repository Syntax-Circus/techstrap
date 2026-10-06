using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// Puts the host behind a reverse proxy, as in production: the connection's peer is the proxy (an address in the trusted network), so the host's forwarded-headers middleware rewrites the
/// client address from the request's <c>X-Forwarded-For</c>. A probe endpoint after the whole application pipeline then calls the Portal's product client from inside that request, which is
/// what a page does, so a test sees the address the API would see.
/// </summary>
internal sealed class ProxyHopStartupFilter(string proxyAddress) : IStartupFilter
{
    public const string ProbePath = "/__probe/product";

    public static Action<IServiceCollection> Add(string proxyAddress = "192.0.2.10") => services => services.AddSingleton<IStartupFilter>(new ProxyHopStartupFilter(proxyAddress));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
            return nextMiddleware(context);
        });
        next(app);

        // Reached only when no endpoint of the Portal matched the path.
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Path != ProbePath)
            {
                await nextMiddleware(context);
                return;
            }

            var client = context.RequestServices.GetRequiredService<IPublicProductClient>();
            var result = await client.GetAsync("paperplane", context.RequestAborted);
            context.Response.StatusCode = result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync(result.IsSuccess ? "ok" : "failed");
        });
    };
}
