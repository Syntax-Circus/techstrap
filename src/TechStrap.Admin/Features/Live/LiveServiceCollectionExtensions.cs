using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Features.Live;

public static class LiveServiceCollectionExtensions
{
    /// <summary>
    /// Registers live updates. <see cref="ITicketLiveClient"/> is scoped (one per circuit, disposed with it): the real client, or the one that does nothing when <c>LiveUpdates:Enabled</c> is off. The connection
    /// factory is a singleton that holds options only; the real client takes the circuit's own token provider and session expiry from its scope.
    /// </summary>
    public static IServiceCollection AddLiveFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LiveUpdatesOptions>()
            .Bind(configuration.GetSection(LiveUpdatesOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILiveConnectionFactory>(sp => new HubLiveConnectionFactory(sp.GetRequiredService<IOptions<SyntaxCircus.Blazor.Auth.ApiOptions>>()));
        services.AddScoped<SignalRTicketLiveClient>();
        services.AddScoped<NullTicketLiveClient>();
        services.AddScoped<ITicketLiveClient>(sp => sp.GetRequiredService<IOptions<LiveUpdatesOptions>>().Value.Enabled
            ? sp.GetRequiredService<SignalRTicketLiveClient>()
            : sp.GetRequiredService<NullTicketLiveClient>());
        return services;
    }
}
