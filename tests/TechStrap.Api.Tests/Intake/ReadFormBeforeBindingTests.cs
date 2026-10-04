using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Intake;

public sealed class ReadFormBeforeBindingTests
{
    private sealed class ThrowingStream(Exception exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw exception;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw exception;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw exception;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<(ResourceExecutingContext Context, bool NextCalled)> RunAsync(string? contentType, Stream body)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = contentType;
        http.Request.Body = body;
        var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);
        var nextCalled = false;
        await new ReadFormBeforeBindingAttribute().OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []));
        });
        return (context, nextCalled);
    }

    [Fact]
    public async Task A_body_cut_short_is_a_400_request_malformed()
    {
        var (context, next) = await RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest)));

        next.ShouldBeFalse();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.Value.ShouldBeOfType<ProblemDetails>().Type.ShouldBe("request-malformed");
    }

    [Fact]
    public async Task A_413_is_left_for_the_request_too_large_middleware()
    {
        await Should.ThrowAsync<BadHttpRequestException>(() => RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge))));
    }

    [Fact]
    public async Task A_413_wrapped_in_another_io_exception_is_still_left_alone()
    {
        await Should.ThrowAsync<IOException>(() => RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new IOException("wrapped", new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge)))));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task A_body_that_is_not_a_form_is_a_415_unsupported_media_type(string? contentType)
    {
        var (context, next) = await RunAsync(contentType, new MemoryStream());

        next.ShouldBeFalse();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status415UnsupportedMediaType);
        result.Value.ShouldBeOfType<ProblemDetails>().Type.ShouldBe("unsupported-media-type");
    }

    [Theory]
    [InlineData(typeof(TechStrap.Api.Controllers.CustomerTicketsController), "Reply")]
    [InlineData(typeof(TechStrap.Api.Controllers.PublicIntakeController), "Submit")]
    [InlineData(typeof(TechStrap.Api.Controllers.TicketsController), "Reply")]
    public void All_three_multipart_routes_carry_the_filter_and_no_consumes_constraint(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;

        method.IsDefined(typeof(ReadFormBeforeBindingAttribute), inherit: false).ShouldBeTrue();
        method.IsDefined(typeof(ConsumesAttribute), inherit: false).ShouldBeFalse();
    }
}
