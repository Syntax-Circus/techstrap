using System.Net;
using System.Net.Sockets;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// A local TCP listener that stands in for an OTLP collector. It never answers; it only counts connections. A leak test points a host's OTLP exporter at
/// <see cref="Endpoint"/> and waits on <see cref="WaitForConnectionAsync"/>: that is the positive control proving an export was really attempted through the
/// <c>HttpClient</c> factory, so "the secret is in no log line" cannot pass just because the exporter never ran.
/// </summary>
public sealed class OtlpProbe : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private readonly Task _accepting;
    private int _connections;

    public OtlpProbe()
    {
        _listener.Start();
        Endpoint = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _accepting = AcceptAsync();
    }

    /// <summary>The URL to hand to <c>OpenTelemetry:OtlpEndpoint</c>.</summary>
    public string Endpoint { get; }

    /// <summary>How many connections the exporter has opened so far.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Waits until at least one connection arrived; false when none did within <paramref name="timeout"/>.</summary>
    public async Task<bool> WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (Connections == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        return Connections > 0;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_clients)
                {
                    _clients.Add(client);
                }

                Interlocked.Increment(ref _connections);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The probe was disposed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }

        _stop.Dispose();
    }
}
