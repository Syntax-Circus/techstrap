using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>Builds <see cref="HubConnection"/>s against an <see cref="ApiFactory"/>'s in-memory server and collects what the hub pushes.</summary>
internal static class HubTestSupport
{
    /// <summary>Generous: a wait is only ever for a message that should arrive, so a pass never waits this long.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static string AgentToken(string subject, string? name = null, DateTime? expires = null) =>
        TestJwt.Token(subject, [TestJwt.AgentGroup], email: $"{subject}@example.com", name: name ?? subject, expires: expires);

    /// <summary>
    /// A connection over long polling on the test server (the transport the in-memory server supports for both directions). The token goes in the Authorization
    /// header through <c>AccessTokenProvider</c>, the way the Admin sends it; <paramref name="queryToken"/> puts one in the URL instead, to prove it is refused.
    /// </summary>
    public static HubConnection Connect(ApiFactory factory, string? headerToken, string? queryToken = null)
    {
        var path = TicketHubRoutes.Path + (queryToken is null ? string.Empty : "?access_token=" + Uri.EscapeDataString(queryToken));
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (headerToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(headerToken);
                }
            })
            .Build();
    }

    /// <summary>
    /// <c>StartAsync</c> returns when the handshake response arrives, which the server sends before <c>OnConnectedAsync</c> has added the connection to the queue group. The hub
    /// handles invocations only after <c>OnConnectedAsync</c> completes, so one answered round trip proves the connection is in the group. <c>LeaveTicket</c> for an unknown
    /// ticket is a harmless refusal (no state changes); the refusal is the answer.
    /// </summary>
    public static async Task ReadyAsync(this HubConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.InvokeAsync(TicketHubMethods.LeaveTicket, Guid.NewGuid(), cancellationToken);
        }
        catch (HubException)
        {
            // Refused as expected: the server answered, so OnConnectedAsync has finished.
        }
    }

    /// <summary>Everything the hub pushes under one method name, in order, readable with a deadline.</summary>
    public sealed class Inbox<T> : IDisposable
    {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
        private readonly IDisposable _subscription;

        public Inbox(HubConnection connection, string method) =>
            _subscription = connection.On<T>(method, message => _channel.Writer.TryWrite(message));

        public async Task<T> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await _channel.Reader.ReadAsync(linked.Token);
        }

        /// <summary>What has already arrived, without waiting.</summary>
        public IReadOnlyList<T> Pending()
        {
            var items = new List<T>();
            while (_channel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        public void Dispose() => _subscription.Dispose();
    }
}
