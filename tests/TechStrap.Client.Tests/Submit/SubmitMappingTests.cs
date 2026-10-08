using System.Net;
using SyntaxCircus.Common;
using TechStrap.Client.Tests.Infrastructure;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Submit;

public sealed class SubmitMappingTests
{
    private const string ProblemJson = "application/problem+json";
    private const string ServerSecret = "db01.internal SELECT secret FROM keys";

    private static async Task<Result<SubmitTicketResponse>> Submit(ClientFixture fixture, CancellationToken ct) =>
        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), ct);

    [Fact(Timeout = 10_000)]
    public async Task A_201_with_a_readable_body_is_a_success()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("TS-1001");
        result.Value.ViewUrl.ShouldBe("https://support.example.com/t/abc");
        result.Value.Warnings.ShouldBeEmpty();
        fixture.Stub.Requests.Count.ShouldBe(1);
    }

    [Theory(Timeout = 10_000)]
    [InlineData("null", "application/json")]
    [InlineData("not json at all", "application/json")]
    [InlineData("[]", "application/json")]
    [InlineData("{}", "application/json")]
    public async Task A_2xx_with_a_missing_or_unreadable_body_is_an_unexpected_response(string body, string mediaType)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.Created, body, mediaType);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(TechStrapClientErrorCodes.UnexpectedResponse);
        error.Kind.ShouldBe(ResultErrorKind.Failure);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_2xx_with_no_body_is_an_unexpected_response()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.Created);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.UnexpectedResponse);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_validation_failure_maps_each_code_to_its_field_with_the_server_message(HttpStatusCode status)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status, """{"detail":"Fix it.","errorCodes":{"email":["required","invalid"],"subject":["too-long"]},"errors":{"email":["Add an email.","Not an email."],"subject":["Too long."]}}""", ProblemJson);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.Errors.Count.ShouldBe(3);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        result.Errors.Select(e => (e.Code, e.Target, e.Message)).ToList().ShouldBe(
        [
            ("required", "email", "Add an email."),
            ("invalid", "email", "Not an email."),
            ("too-long", "subject", "Too long."),
        ]);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_validation_error_with_an_empty_field_name_has_no_target()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadRequest, """{"errorCodes":{"":["body-required"]},"errors":{"":["Send a body."]}}""", ProblemJson);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("body-required");
        error.Target.ShouldBeNull();
    }

    [Fact(Timeout = 10_000)]
    public async Task A_400_without_codes_is_validation_failed_with_the_detail()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadRequest, """{"detail":"Add a subject."}""", ProblemJson);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(TechStrapClientErrorCodes.ValidationFailed);
        error.Kind.ShouldBe(ResultErrorKind.Validation);
        error.Message.ShouldBe("Add a subject.");
    }

    [Theory(Timeout = 10_000)]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task A_400_without_codes_or_detail_uses_the_fixed_copy(string? body)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(HttpStatusCode.BadRequest, body, ProblemJson);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(TechStrapClientErrorCodes.ValidationFailed);
        error.Message.ShouldBe(TechStrapClientMessages.Invalid);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.Unauthorized, TechStrapClientErrorCodes.InvalidApiKey, ResultErrorKind.Unauthenticated)]
    [InlineData(HttpStatusCode.Forbidden, TechStrapClientErrorCodes.InvalidApiKey, ResultErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, TechStrapClientErrorCodes.PayloadTooLarge, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, TechStrapClientErrorCodes.UnsupportedMediaType, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.TooManyRequests, TechStrapClientErrorCodes.RateLimited, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.InternalServerError, TechStrapClientErrorCodes.ApiUnavailable, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.NotFound, TechStrapClientErrorCodes.ApiError, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.Conflict, TechStrapClientErrorCodes.ApiError, ResultErrorKind.Failure)]
    [InlineData(HttpStatusCode.Found, TechStrapClientErrorCodes.ApiError, ResultErrorKind.Failure)]
    public async Task A_single_status_maps_to_its_code_and_kind(HttpStatusCode status, string code, ResultErrorKind kind)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status, "{}", ProblemJson);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Kind.ShouldBe(kind);
        error.Target.ShouldBeNull();
    }

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_retryable_status_that_stays_failing_ends_as_api_unavailable(HttpStatusCode status)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status, "{}", ProblemJson, times: 3);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        error.Kind.ShouldBe(ResultErrorKind.Failure);
        fixture.Stub.Requests.Count.ShouldBe(3);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.Found)]
    public async Task Server_text_of_non_400_responses_never_reaches_the_result(HttpStatusCode status)
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Respond(status, $$$"""{"detail":"{{{ServerSecret}}}","title":"{{{ServerSecret}}}","errors":{"x":["{{{ServerSecret}}}"]}}""", ProblemJson, times: 3);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldAllBe(e => !e.Message.Contains("db01", StringComparison.Ordinal) && !e.Message.Contains("SELECT", StringComparison.Ordinal));
    }

    [Fact(Timeout = 10_000)]
    public async Task A_transport_failure_ends_as_api_unavailable_without_the_exception_text()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Throw(() => new HttpRequestException("Connection refused to db01.internal:5432"), times: 3);

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        error.Message.ShouldNotContain("db01");
        error.Message.ShouldNotContain("5432");
    }

    [Fact(Timeout = 10_000)]
    public async Task A_timeout_exception_from_the_handler_ends_as_api_unavailable()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 1);
        fixture.Stub.Throw(() => new TimeoutException("slow"));

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_cancellation_that_is_not_the_callers_ends_as_api_unavailable()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 1);
        fixture.Stub.Throw(() => new TaskCanceledException("handler gave up"));

        var result = await Submit(fixture, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
    }
}
