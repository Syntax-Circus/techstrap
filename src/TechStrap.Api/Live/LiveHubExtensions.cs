using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Live;

public static class LiveHubExtensions
{
    /// <summary>
    /// SignalR, the in-memory presence store (D-007: one Api instance), the hub's <see cref="ITicketChangeBroadcaster"/> and the listener that relays the Worker's NOTIFY to it. Call it after <c>AddTechStrapPersistence</c>:
    /// that registered the null broadcaster, and <c>Replace</c> is what makes the hub's win.
    /// </summary>
    public static IServiceCollection AddTechStrapLiveHub(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddTechStrapPresence();
        services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>());
        services.AddTechStrapTicketChangeListener();
        return services;
    }

    /// <summary>
    /// Maps the hub at <see cref="TicketHubRoutes.Path"/> for agents only, closing a connection when its token expires (the JWT is validated at the handshake only).
    /// The explicit policy is deliberate although the fallback policy would also require a sign-in.
    /// </summary>
    public static IEndpointConventionBuilder MapTechStrapLiveHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<TicketHub>(TicketHubRoutes.Path, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization(AuthorizationPolicies.Agent);
}
