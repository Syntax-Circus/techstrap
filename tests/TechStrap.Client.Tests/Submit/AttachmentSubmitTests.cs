using System.Net;
using TechStrap.Client.Tests.Infrastructure;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Submit;

public sealed class AttachmentSubmitTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class BrokenStream(bool failOnDispose) : MemoryStream([65])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            failOnDispose ? base.ReadAsync(buffer, cancellationToken) : throw new IOException("private read failure");
        public override ValueTask DisposeAsync()
        {
            base.Dispose();
            return failOnDispose ? ValueTask.FromException(new IOException("private close failure")) : ValueTask.CompletedTask;
        }
    }

    private sealed class HangingReadStream : MemoryStream
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); Disposal.TrySetResult(); }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("http")]
    [InlineData("permission")]
    public async Task Source_exceptions_never_become_transport_retries_or_leak(string kind)
    {
        using var fixture = new ClientFixture();
        var opens = 0;
        var file = new TicketAttachment("a.log", "text/plain", _ =>
        {
            opens++;
            throw kind switch
            {
                "timeout" => new TimeoutException("private path"),
                "http" => new HttpRequestException("private path"),
                _ => new System.Security.SecurityException("private path"),
            };
        });
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct);
        result.Errors[0].Code.ShouldBe("attachment-read-failed");
        opens.ShouldBe(1);
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancellation_during_read_disposes_the_source_and_propagates()
    {
        using var fixture = new ClientFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stream = new HangingReadStream();
        var file = new TicketAttachment("a.log", "text/plain", _ => ValueTask.FromResult<Stream>(stream));
        var call = fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], cancellation.Token);
        await stream.Reading.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);
        await stream.Disposal.Task.WaitAsync(TestContext.Current.CancellationToken);
        stream.Disposed.ShouldBeTrue();
    }

    [Fact(Timeout = 10_000)]
    public async Task Total_timeout_includes_reading_and_disposes_the_source()
    {
        using var fixture = new ClientFixture(o => o.Timeout = TimeSpan.FromSeconds(10), fakeTime: true);
        var stream = new HangingReadStream();
        var file = new TicketAttachment("a.log", "text/plain", _ => ValueTask.FromResult<Stream>(stream));
        var call = fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], TestContext.Current.CancellationToken);
        await stream.Reading.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Time!.Advance(TimeSpan.FromSeconds(11));
        var result = await call;
        result.Errors[0].Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        // The pipeline bounds the call, then canceled serialization unwinds asynchronously.
        await stream.Disposal.Task.WaitAsync(TestContext.Current.CancellationToken);
        stream.Disposed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_read_and_close_failures_are_safe_and_not_retried(bool failOnDispose)
    {
        using var fixture = new ClientFixture();
        var opens = 0;
        var file = new TicketAttachment("a.log", "text/plain", _ =>
        {
            opens++;
            return ValueTask.FromResult<Stream>(new BrokenStream(failOnDispose));
        });
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct);
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-read-failed");
        result.Errors[0].Message.ShouldNotContain("private");
        opens.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_lengths_obey_the_combined_budget()
    {
        using var fixture = new ClientFixture();
        var streams = new List<TrackedStream>();
        var files = Enumerable.Range(0, 3).Select(i => new TicketAttachment($"{i}.log", "text/plain", _ =>
        {
            var stream = new TrackedStream(new byte[9 * 1024 * 1024]);
            streams.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        })).ToArray();
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), files, Ct);
        result.Errors[0].Code.ShouldBe("attachments-too-large");
        streams.ShouldAllBe(s => s.Disposed);
        streams.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(6, 1, "attachments-too-many")]
    [InlineData(3, 9 * 1024 * 1024, "attachments-too-large")]
    public async Task Count_and_known_total_size_are_checked_before_opening(int count, long length, string code)
    {
        using var fixture = new ClientFixture();
        var file = new TicketAttachment("a.log", "text/plain", _ => throw new InvalidOperationException("must not open"), length);
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), Enumerable.Repeat(file, count).ToArray(), Ct);
        result.Errors[0].Code.ShouldBe(code);
        fixture.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Local_file_helper_reopens_from_the_beginning()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        try
        {
            await File.WriteAllTextAsync(path, "local file", Ct);
            var file = TicketAttachment.FromFile(path, "text/plain");
            using var fixture = new ClientFixture();
            fixture.Stub.Respond(HttpStatusCode.BadGateway).Created();
            (await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct)).IsSuccess.ShouldBeTrue();
            fixture.Stub.Requests.ShouldAllBe(r => r.Body!.Contains("local file"));
            fixture.Stub.Requests.Count.ShouldBe(2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Retry_reopens_streams_and_resends_the_same_bytes_and_key()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadGateway).Created();
        var streams = new List<TrackedStream>();
        var attachment = new TicketAttachment("trace.log", "text/plain", ct =>
        {
            ct.ThrowIfCancellationRequested();
            var stream = new TrackedStream("SDK file contents"u8.ToArray());
            streams.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        });
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [attachment], "upload-1", Ct);
        result.IsSuccess.ShouldBeTrue();
        streams.Count.ShouldBe(2);
        streams.ShouldAllBe(s => s.Disposed);
        fixture.Stub.Requests.Count.ShouldBe(2);
        fixture.Stub.Requests.ShouldAllBe(r => r.Uri!.AbsolutePath == "/api/intake/tickets/with-attachments"
            && r.Header(HeaderNames.IdempotencyKey) == "upload-1" && r.Body!.Contains("SDK file contents"));
    }

    [Fact]
    public async Task Empty_attachment_list_preserves_json_route()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();
        (await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [], Ct)).IsSuccess.ShouldBeTrue();
        fixture.Stub.Requests.ShouldHaveSingleItem().Uri!.AbsolutePath.ShouldBe("/api/intake/tickets");
    }

    [Fact]
    public async Task Once_with_files_never_retries_or_sends_a_key()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadGateway).Created();
        var file = new TicketAttachment("a.log", "text/plain", _ => ValueTask.FromResult<Stream>(new MemoryStream([65])));
        (await fixture.Client.SubmitTicketOnceAsync(ClientFixture.Request(), [file], Ct)).IsFailure.ShouldBeTrue();
        fixture.Stub.Requests.ShouldHaveSingleItem().Header(HeaderNames.IdempotencyKey).ShouldBeNull();
    }

    [Fact]
    public async Task Local_open_failure_is_safe_and_not_retried()
    {
        using var fixture = new ClientFixture();
        var opens = 0;
        var file = new TicketAttachment("a.log", "text/plain", _ =>
        {
            opens++;
            throw new IOException("secret-local-path");
        });
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct);
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-read-failed");
        result.Errors[0].Message.ShouldNotContain("secret-local-path");
        opens.ShouldBe(1);
    }

    [Theory]
    [InlineData("a.exe", 1, "attachment-type-not-allowed")]
    [InlineData("a.log", IntakeLimits.MaxFileBytes + 1, "attachment-too-large")]
    public async Task Invalid_attachments_are_refused_before_opening(string name, long length, string code)
    {
        using var fixture = new ClientFixture();
        var opens = 0;
        var file = new TicketAttachment(name, "text/plain", _ => { opens++; return ValueTask.FromResult<Stream>(Stream.Null); }, length);
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct);
        result.Errors[0].Code.ShouldBe(code);
        opens.ShouldBe(0);
        fixture.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_length_streams_are_bounded_and_disposed()
    {
        using var fixture = new ClientFixture();
        var stream = new TrackedStream(new byte[IntakeLimits.MaxFileBytes + 1]);
        var file = new TicketAttachment("a.log", "text/plain", _ => ValueTask.FromResult<Stream>(stream));
        var result = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], Ct);
        result.Errors[0].Code.ShouldBe("attachment-too-large");
        stream.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_during_open_propagates_and_never_sends()
    {
        using var fixture = new ClientFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var file = new TicketAttachment("a.log", "text/plain", async ct =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Stream.Null;
        });
        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Client.SubmitTicketAsync(ClientFixture.Request(), [file], cancellation.Token));
        fixture.Stub.Requests.ShouldBeEmpty();
    }
}
