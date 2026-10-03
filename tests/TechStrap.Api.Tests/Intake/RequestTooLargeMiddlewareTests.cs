using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Intake;

public sealed class RequestTooLargeMiddlewareTests
{
    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    private static async Task<(int Status, string? Type, string ContentType)> Read(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, json.RootElement.GetProperty("type").GetString(), context.Response.ContentType ?? string.Empty);
    }

    private static BadHttpRequestException TooLarge() => new("too large", StatusCodes.Status413PayloadTooLarge);

    [Fact]
    public async Task A_413_bad_request_exception_becomes_a_problem_response()
    {
        var context = NewContext();

        await new RequestTooLargeMiddleware(_ => throw TooLarge()).InvokeAsync(context);

        var (status, type, contentType) = await Read(context);
        status.ShouldBe(413);
        type.ShouldBe("request-too-large");
        contentType.ShouldContain("application/problem+json");
    }

    [Fact]
    public async Task A_wrapped_413_exception_becomes_a_problem_response()
    {
        var context = NewContext();

        await new RequestTooLargeMiddleware(_ => throw new InvalidOperationException("wrapper", TooLarge())).InvokeAsync(context);

        (await Read(context)).Status.ShouldBe(413);
    }

    [Fact]
    public async Task Other_exceptions_are_rethrown_untouched()
    {
        var context = NewContext();
        var original = new BadHttpRequestException("bad", StatusCodes.Status400BadRequest);

        var thrown = await Should.ThrowAsync<BadHttpRequestException>(new RequestTooLargeMiddleware(_ => throw original).InvokeAsync(context));

        thrown.ShouldBeSameAs(original);
        context.Response.Body.Length.ShouldBe(0);
    }

    [Fact]
    public async Task Nothing_is_written_once_the_response_has_started()
    {
        var context = NewContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Should.ThrowAsync<BadHttpRequestException>(new RequestTooLargeMiddleware(_ => throw TooLarge()).InvokeAsync(context));

        context.Response.Body.Length.ShouldBe(0);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
