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

    private static readonly byte[] Continue = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

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

    /// <summary>
    /// Reads one request, answers 200 and closes the connection. The body is read exactly: by its declared <c>Content-Length</c>, or chunk by chunk from the chunk sizes (never by what
    /// the data happens to end with). A request that says <c>Expect: 100-continue</c> gets <c>100 Continue</c> first, because its sender waits for it before sending the body.
    /// </summary>
    private async Task AnswerAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var pending = new List<byte>();

                string headers;
                while (!TryTakeHeaders(pending, out headers))
                {
                    if (!await FillAsync(stream, pending))
                    {
                        return;
                    }
                }

                if (headers.Contains("Expect: 100-continue", StringComparison.OrdinalIgnoreCase))
                {
                    await stream.WriteAsync(Continue, _stop.Token);
                }

                if (headers.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
                {
                    await SkipChunkedBodyAsync(stream, pending);
                }
                else
                {
                    await SkipAsync(stream, pending, ContentLength(headers));
                }

                await stream.WriteAsync(Response, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException or EndOfStreamException or FormatException or OverflowException)
        {
            // The client went away, or the probe was disposed.
        }
    }

    /// <summary>Takes the header block (up to the blank line) off the front of <paramref name="pending"/>; false when it has not all arrived yet.</summary>
    private static bool TryTakeHeaders(List<byte> pending, out string headers)
    {
        var text = Encoding.ASCII.GetString([.. pending]);
        var end = text.IndexOf(HeaderEnd, StringComparison.Ordinal);
        headers = end < 0 ? string.Empty : text[..end];
        if (end >= 0)
        {
            pending.RemoveRange(0, end + HeaderEnd.Length);
        }

        return end >= 0;
    }

    private async Task<bool> FillAsync(NetworkStream stream, List<byte> pending)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, _stop.Token);
        pending.AddRange(buffer.AsSpan(0, read).ToArray());
        return read > 0;
    }

    /// <summary>Discards <paramref name="count"/> bytes, reading more from the stream as needed.</summary>
    private async Task SkipAsync(NetworkStream stream, List<byte> pending, int count)
    {
        while (pending.Count < count)
        {
            if (!await FillAsync(stream, pending))
            {
                throw new EndOfStreamException();
            }
        }

        pending.RemoveRange(0, count);
    }

    /// <summary>Takes one CRLF-terminated line off the front of <paramref name="pending"/>, reading more from the stream as needed.</summary>
    private async Task<string> ReadLineAsync(NetworkStream stream, List<byte> pending)
    {
        while (true)
        {
            for (var i = 0; i + 1 < pending.Count; i++)
            {
                if (pending[i] == '\r' && pending[i + 1] == '\n')
                {
                    var line = Encoding.ASCII.GetString([.. pending.Take(i)]);
                    pending.RemoveRange(0, i + 2);
                    return line;
                }
            }

            if (!await FillAsync(stream, pending))
            {
                throw new EndOfStreamException();
            }
        }
    }

    /// <summary>Each chunk is a hexadecimal size line (any extension after <c>;</c> is ignored), that many bytes and a CRLF; a size of 0 is followed by optional trailers and a blank line.</summary>
    private async Task SkipChunkedBodyAsync(NetworkStream stream, List<byte> pending)
    {
        while (true)
        {
            var sizeLine = await ReadLineAsync(stream, pending);
            var size = int.Parse(sizeLine.Split(';')[0].Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            if (size == 0)
            {
                while (await ReadLineAsync(stream, pending) is { Length: > 0 })
                {
                    // A trailer header: not needed.
                }

                return;
            }

            await SkipAsync(stream, pending, size + 2);
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
