using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// A local TCP listener that stands in for an OTLP collector. It counts connections and answers every request <c>200 OK</c> with an empty body, then closes the connection, so
/// an exporter's final flush at host shutdown finishes at once instead of waiting for its HTTP timeout. A leak test points a host's OTLP exporter at <see cref="Endpoint"/> and
/// waits on <see cref="WaitForConnectionAsync"/>: that is the positive control proving an export was really attempted, so "the secret is in no log line" cannot pass just
/// because the exporter never ran.
/// </summary>
public sealed class OtlpProbe : IAsyncDisposable
{
    private const string HeaderEnd = "\r\n\r\n";

    private static readonly byte[] Response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _handlers = [];
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
                Interlocked.Increment(ref _connections);
                lock (_handlers)
                {
                    _handlers.Add(AnswerAsync(client));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The probe was disposed.
        }
    }

    /// <summary>Reads one request (the headers, then a body of the declared length, or to the end of a chunked body), answers 200 and closes the connection.</summary>
    private async Task AnswerAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var received = new List<byte>();
                var buffer = new byte[8192];
                var headerEnd = -1;
                var chunked = false;
                var needed = int.MaxValue;
                while (received.Count < needed)
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    received.AddRange(buffer.AsSpan(0, read).ToArray());
                    if (headerEnd < 0)
                    {
                        var text = Encoding.ASCII.GetString([.. received]);
                        var end = text.IndexOf(HeaderEnd, StringComparison.Ordinal);
                        if (end >= 0)
                        {
                            headerEnd = end + HeaderEnd.Length;
                            chunked = text[..end].Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase);
                            needed = chunked ? int.MaxValue : headerEnd + ContentLength(text[..end]);
                        }
                    }

                    if (chunked && Encoding.ASCII.GetString([.. received], headerEnd, received.Count - headerEnd).EndsWith("0\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                await stream.WriteAsync(Response, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // The client went away, or the probe was disposed.
        }
    }

    private static int ContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line["Content-Length:".Length..].Trim(), out var length))
            {
                return length;
            }
        }

        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        Task[] handlers;
        lock (_handlers)
        {
            handlers = [.. _handlers];
        }

        await Task.WhenAll(handlers);
        _stop.Dispose();
    }
}
