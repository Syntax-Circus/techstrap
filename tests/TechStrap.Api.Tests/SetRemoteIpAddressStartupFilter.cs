using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TechStrap.Api.Tests;

/// <summary>
/// TestServer has no real peer, so RemoteIpAddress is null. This filter runs before anything Program.cs
/// adds (including UseForwardedHeaders), so it can stand in for the connection's peer address.
/// </summary>
public sealed class SetRemoteIpAddressStartupFilter(IPAddress remoteIp) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIp;
                return nextMiddleware();
            });
            next(app);
        };
}
