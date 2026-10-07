using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The transport the Admin really uses. The in-memory test server cannot carry a WebSocket handshake header, so this one test starts the host on a real loopback
/// Kestrel port and connects with the real client over WebSockets and only the Authorization header: it proves the header token is enough on that transport, which is
/// why the hub needs no query-token support (D-046). The listener stops with the factory.
/// </summary>
public sealed class HubWebSocketTests(TestPostgres postgres)
{
    [Fact(Timeout = 120000)]
    public async Task A_header_token_is_enough_over_real_web_sockets_and_a_change_arrives()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var factory = new ApiFactory(settings: settings);
        factory.UseKestrel(0);
        factory.StartServer();
        await TicketTestData.SeedAsync(factory, ct);
        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(address, TicketHubRoutes.Path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.AccessTokenProvider = () => Task.FromResult<string?>(HubTestSupport.AgentToken("sam", "Sam"));
            })
            .Build();
        using var inbox = new HubTestSupport.Inbox<TicketChangedDto>(connection, TicketHubMethods.TicketChanged);
        await connection.StartAsync(ct);
        var change = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-1", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, ct);

        (await inbox.NextAsync()).EventId.ShouldBe(change.EventId);

        // The same handshake with no token, and with the token only in the query (the browser habit), is refused.
        foreach (var url in new[] { TicketHubRoutes.Path, TicketHubRoutes.Path + "?access_token=" + Uri.EscapeDataString(HubTestSupport.AgentToken("sam", "Sam")) })
        {
            await using var refused = new HubConnectionBuilder()
                .WithUrl(new Uri(address, url), options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
                .Build();
            await Should.ThrowAsync<Exception>(() => refused.StartAsync(ct));
        }
    }
}
