using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed class AttachmentPassThroughTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static HttpResponseMessage File(byte[] bytes, string contentType, string disposition)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        response.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(disposition);
        return response;
    }

    [Fact]
    public async Task The_file_is_streamed_for_the_signed_in_agent_as_a_forced_download()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        var bytes = "<svg onload=alert(1)></svg>"u8.ToArray();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => File(bytes, "image/svg+xml", "inline; filename=\"logo.svg\""));
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("logo.svg");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        // The agent's bearer token went to the API, and nowhere in the answer.
        factory.Api.Requests.Last(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal)).Authorization.ShouldBe("Bearer " + AdminTestPrincipal.Agent.AccessToken);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        response.ToString().ShouldNotContain(AdminTestPrincipal.Agent.AccessToken);
    }

    [Fact]
    public async Task The_body_is_streamed_not_buffered()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        var release = new TaskCompletionSource();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream([1, 2, 3, 4], release.Task)) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            return response;
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // If the Admin read the whole file before answering, the stream would never be released and this would time out.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync($"/attachments/{id}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var first = new byte[2];
        await body.ReadExactlyAsync(first, timeout.Token);
        release.SetResult();
        var rest = new MemoryStream();
        await body.CopyToAsync(rest, timeout.Token);

        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        first.ShouldBe([1, 2]);
        rest.ToArray().ShouldBe([3, 4]);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(403, 403)]
    [InlineData(401, 401)]
    [InlineData(400, 502)]
    public async Task An_upstream_failure_is_passed_on_without_the_api_detail(int upstream, int expected)
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.OnProblem(HttpMethod.Get, $"/api/attachments/{id}", (HttpStatusCode)upstream, "attachment-not-found", "Secret detail from the API.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        ((int)response.StatusCode).ShouldBe(expected);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Secret detail from the API.");
        response.ToString().ShouldNotContain(AdminTestPrincipal.Agent.AccessToken);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        string.Concat(cookies ?? []).ShouldNotContain(AdminTestPrincipal.Agent.AccessToken);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_anonymous_request_is_sent_to_sign_in_and_never_reaches_the_api()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync($"/attachments/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        factory.Api.Requests.ShouldNotContain(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_a_guid_is_accepted_as_the_id()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync("/attachments/..%2Fagents%2Fme", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.Requests.ShouldNotContain(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal));
    }

    /// <summary>Serves the first chunk at once and the rest only after <paramref name="gate"/> completes.</summary>
    private sealed class GatedStream(byte[] data, Task gate) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length)
            {
                return 0;
            }

            if (_position >= 2)
            {
                await gate.WaitAsync(cancellationToken);
            }

            var count = Math.Min(Math.Min(buffer.Length, data.Length - _position), _position < 2 ? 2 : int.MaxValue);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
