using System.Net.Sockets;
using System.Text;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>The probe stands in for a collector, so it has to read a request the way an HTTP client sends it: by length, chunked, or after a <c>100 Continue</c>.</summary>
public sealed class OtlpProbeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(TcpClient Client, NetworkStream Stream)> ConnectAsync(OtlpProbe probe)
    {
        var uri = new Uri(probe.Endpoint);
        var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port, Ct);
        return (client, client.GetStream());
    }

    private static Task SendAsync(NetworkStream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text), Ct).AsTask();

    /// <summary>Reads what the probe sends until it closes the connection, or fails the test after five seconds.</summary>
    private static async Task<string> ReadToEndAsync(NetworkStream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync(timeout.Token);
    }

    [Fact]
    public async Task A_body_of_the_declared_length_is_read_whole_before_the_answer()
    {
        await using var probe = new OtlpProbe();
        var (client, stream) = await ConnectAsync(probe);
        using var _ = client;

        await SendAsync(stream, "POST /v1/traces HTTP/1.1\r\nHost: x\r\nContent-Length: 10\r\n\r\n01234");
        await Task.Delay(200, Ct);
        stream.DataAvailable.ShouldBeFalse("the probe must wait for the rest of the body");
        await SendAsync(stream, "56789");

        (await ReadToEndAsync(stream)).ShouldStartWith("HTTP/1.1 200 OK");
    }

    [Fact]
    public async Task A_chunked_body_is_read_by_its_chunk_sizes_and_not_by_what_the_data_looks_like()
    {
        await using var probe = new OtlpProbe();
        var (client, stream) = await ConnectAsync(probe);
        using var _ = client;

        // The first chunk's data ends with the five characters of a terminating chunk, and the bytes received so far end with them too; only the chunk sizes say the body goes on.
        await SendAsync(stream, "POST /v1/traces HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n7\r\nab0\r\n\r\n");
        await Task.Delay(200, Ct);
        stream.DataAvailable.ShouldBeFalse("a chunk that merely ends in 0 CRLF CRLF is not the end of the body");
        await SendAsync(stream, "\r\n3\r\nxyz\r\n0\r\n\r\n");

        (await ReadToEndAsync(stream)).ShouldStartWith("HTTP/1.1 200 OK");
    }

    [Fact]
    public async Task Expect_100_continue_is_answered_before_the_body_is_sent()
    {
        await using var probe = new OtlpProbe();
        var (client, stream) = await ConnectAsync(probe);
        using var _ = client;

        await SendAsync(stream, "POST /v1/traces HTTP/1.1\r\nHost: x\r\nExpect: 100-continue\r\nContent-Length: 4\r\n\r\n");
        var interim = new byte[64];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var read = await stream.ReadAsync(interim, timeout.Token);
        Encoding.ASCII.GetString(interim, 0, read).ShouldStartWith("HTTP/1.1 100 Continue");

        await SendAsync(stream, "body");

        (await ReadToEndAsync(stream)).ShouldStartWith("HTTP/1.1 200 OK");
    }
}
