using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Tests.Shared;

internal static class LoopbackKestrel
{
    /// <summary>
    /// Runs the factory's host on a real Kestrel socket, on a free loopback port. Call it before the host starts. Use this, never <c>UseKestrel(port)</c>: that overload rewrites the server's addresses after the
    /// host is built, while the app's own thread is already on its way to <c>app.Run()</c>. When the server starts first, the rewrite throws "Addresses cannot be modified after the server has started". An
    /// endpoint in the options is in place before anything can start.
    /// </summary>
    public static void UseLoopbackKestrel<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class =>
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
}
