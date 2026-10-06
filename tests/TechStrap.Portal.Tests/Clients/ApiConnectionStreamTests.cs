using System.Net;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// <see cref="ApiConnection.OpenStreamAsync"/>: the one streaming GET of the Portal (the attachment pass-through). It sends the ticket token like any customer call, goes through the retrying read client,
/// reads only the response headers before it returns (the body is never buffered), maps every failure to a <see cref="Result"/> and gives the caller a body it must dispose.
/// </summary>
public sealed class ApiConnectionStreamTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Text = new('t', TicketToken.Length);
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string Path = $"/api/customer/attachments/{Id}";

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }

        public bool Closed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent(byte[] bytes) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static TicketToken Token()
    {
        TicketToken.TryParse(Text, out var token).ShouldBeTrue();
        return token;
    }

    [Fact]
    public async Task A_download_carries_the_token_through_the_read_client_and_gives_the_body_type_length_and_name()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(new MemoryStream("hello"u8.ToArray()), "text/plain", "log.txt"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        await using var download = result.Value;
        download.ContentType.ShouldBe("text/plain");
        download.ContentLength.ShouldBe(5);
        download.FileName.ShouldBe("log.txt");
        using var reader = new StreamReader(download.Body);
        (await reader.ReadToEndAsync(Ct)).ShouldBe("hello");
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.TicketToken.ShouldBe(Text);
        request.Client.ShouldBe(ApiClientNames.Read);
        request.Query.ShouldBeEmpty();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task Only_the_response_headers_are_read_before_the_call_returns()
    {
        using var api = ApiHarness.Create();
        var body = new CountingStream(new byte[100_000]);
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(body, "application/octet-stream"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        body.Reads.ShouldBe(0, "the default completion option would have buffered the whole body here");
        await using var download = result.Value;
        (await download.Body.ReadAsync(new byte[10], Ct)).ShouldBeGreaterThan(0);
        body.Reads.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_missing_content_type_is_octet_stream_and_a_missing_name_is_null()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        await using var download = result.Value;
        download.ContentType.ShouldBe("application/octet-stream");
        download.FileName.ShouldBeNull();
    }

    [Fact]
    public async Task Disposing_the_download_closes_the_upstream_body_and_the_response()
    {
        using var api = ApiHarness.Create();
        var body = new CountingStream([1, 2, 3]);
        var content = new TrackingContent([1, 2, 3]);
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(body, "application/octet-stream"));
        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        body.Closed.ShouldBeFalse();
        await result.Value.DisposeAsync();

        body.Closed.ShouldBeTrue();

        api.Stub.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var second = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        content.Disposed.ShouldBeFalse("the response stays open while the caller streams it");
        await second.Value.DisposeAsync();

        content.Disposed.ShouldBeTrue("disposing the download releases the upstream response, so its connection goes back to the pool");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorCodes.ApiError)]
    public async Task A_failure_is_a_result_error_decided_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, Path, status, "x", "Npgsql host=10.0.0.5 stack trace");

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsFailure.ShouldBeTrue();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Message.ShouldNotContain("Npgsql");
    }

    [Fact]
    public async Task A_503_is_retried_twice_like_any_read_and_every_attempt_carries_the_token()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.ServiceUnavailable);

        var unavailable = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        unavailable.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, Path).ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Text, "every attempt carries the token");
    }

    [Fact]
    public async Task A_transport_failure_is_api_unavailable_with_no_exception_text()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        error.Message.ShouldNotContain("10.1.2.3");
    }

    [Fact]
    public async Task A_cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(new MemoryStream([1]), "text/plain"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), cancelled.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_failed_answer_is_disposed_before_the_error_is_returned(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        var contents = new List<TrackingContent>();
        api.Stub.On(HttpMethod.Get, Path, _ =>
        {
            var content = new TrackingContent("{}"u8.ToArray());
            contents.Add(content);
            return new HttpResponseMessage(status) { Content = content };
        });

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsFailure.ShouldBeTrue();
        contents.ShouldNotBeEmpty();
        contents.ShouldAllBe(c => c.Disposed, "nothing is handed to the caller on a failure, so nothing may be left open (a leaked response holds a pooled connection)");
    }

    [Fact]
    public async Task Disposing_a_download_whose_body_throws_still_disposes_the_response_and_the_request()
    {
        var content = new TrackingContent([1]);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var requestContent = new TrackingContent([1]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/x") { Content = requestContent };
        var download = new ApiDownload(request, response, new ThrowingStream());

        await Should.ThrowAsync<InvalidOperationException>(async () => await download.DisposeAsync());

        content.Disposed.ShouldBeTrue("the response is released even when closing the body fails");
        requestContent.Disposed.ShouldBeTrue("and so is the request");
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override ValueTask DisposeAsync() => throw new InvalidOperationException("the body will not close");

        protected override void Dispose(bool disposing) => throw new InvalidOperationException("the body will not close");
    }
}
