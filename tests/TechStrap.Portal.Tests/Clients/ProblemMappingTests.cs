using System.Net;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>P09-T02: every status the Portal must treat differently maps to one code, one kind and one fixed, user-safe message.</summary>
public sealed class ProblemMappingTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<ResultError>> MapAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task A_400_yields_every_field_error_with_its_code_message_and_target()
    {
        var errors = await MapAsync(StubApiHandler.ValidationProblem(
        [
            ("email", "email-invalid", "That email address does not look right."),
            ("email", "email-too-long", "That email address is too long."),
            ("subject", "subject-required", "Add a subject."),
            ("", "attachments-too-many", "Attach at most 5 files."),
        ]));

        errors.Count.ShouldBe(4);
        errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        errors.Where(e => e.Target == "email").Select(e => e.Code).ShouldBe(["email-invalid", "email-too-long"]);
        errors.Single(e => e.Target == "subject").Message.ShouldBe("Add a subject.");
        errors.Single(e => e.Code == "attachments-too-many").Target.ShouldBeNull();
    }

    [Fact]
    public async Task A_400_without_field_codes_is_one_validation_failure_with_the_detail_as_its_message()
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.BadRequest, "bad-request", "The request body is not valid."));

        var error = errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Validation);
        error.Code.ShouldBe(ApiErrorCodes.ValidationFailed);
        error.Message.ShouldBe("The request body is not valid.");
        error.Target.ShouldBeNull();
    }

    [Fact]
    public async Task A_400_with_no_body_gets_the_fixed_copy()
    {
        var errors = await MapAsync(new HttpResponseMessage(HttpStatusCode.BadRequest));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ValidationFailed, ProblemCopy.Invalid, ResultErrorKind.Validation));
    }

    // Product enumeration: whatever the API says about a missing thing, the Portal says one thing, with one code.
    [Theory]
    [InlineData("product-not-found")]
    [InlineData("not-found")]
    [InlineData("ticket-not-found")]
    [InlineData("a-code-the-api-may-add-later")]
    public async Task A_404_is_always_the_same_not_found_error_whatever_the_api_called_it(string type)
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.NotFound, type, "No such Paperplane product."));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task A_413_is_payload_too_large_and_a_415_is_unsupported_media_type_with_fixed_copy()
    {
        var tooLarge = await MapAsync(StubApiHandler.Problem(HttpStatusCode.RequestEntityTooLarge, "request-too-large", "Body larger than 26214400 bytes."));
        var unsupported = await MapAsync(StubApiHandler.Problem(HttpStatusCode.UnsupportedMediaType, "unsupported-media-type", "This endpoint accepts multipart/form-data."));

        tooLarge.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure));
        unsupported.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.UnsupportedMediaType, ProblemCopy.UnsupportedMediaType, ResultErrorKind.Failure));
    }

    [Fact]
    public async Task A_429_is_rate_limited_with_fixed_copy()
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.TooManyRequests, "rate-limited", "Too many requests."));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.RateLimited, ProblemCopy.RateLimited, ResultErrorKind.Failure));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(599)]
    public async Task Any_5xx_is_api_unavailable_decided_by_status_never_by_the_apis_own_text(int status)
    {
        var errors = await MapAsync(StubApiHandler.Problem((HttpStatusCode)status, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5"));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ApiUnavailable, ProblemCopy.ApiUnavailable, ResultErrorKind.Failure));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(418)]
    public async Task Any_other_status_is_a_generic_api_error_with_fixed_copy(int status)
    {
        var errors = await MapAsync(StubApiHandler.Problem((HttpStatusCode)status, "something", "internal detail"));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ApiError, ProblemCopy.ApiError, ResultErrorKind.Failure));
    }

    [Fact]
    public async Task A_409_is_the_reply_conflict_with_its_own_fixed_copy_whatever_the_api_said()
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.Conflict, "reply-conflict", "internal detail: rowversion 17 != 18"));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict));
        errors[0].Message.ShouldNotContain("rowversion");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"errorCodes\":[1]}")]
    [InlineData("{\"errorCodes\":{\"a\":\"b\"}}")]
    public async Task An_unreadable_problem_body_never_throws(string body)
    {
        foreach (var status in new[] { 400, 404, 413, 429, 500 })
        {
            using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };

            var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));

            errors.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Every_fixed_message_is_plain_text_a_visitor_can_act_on()
    {
        foreach (var message in new[] { ProblemCopy.Invalid, ProblemCopy.NotFound, ProblemCopy.PayloadTooLarge, ProblemCopy.UnsupportedMediaType, ProblemCopy.RateLimited, ProblemCopy.ApiUnavailable, ProblemCopy.ApiError, ProblemCopy.UnexpectedResponse, ProblemCopy.ReplyConflict })
        {
            message.ShouldNotBeNullOrWhiteSpace();
            message.ShouldNotContain("<");
            message.ShouldNotContain("API", Case.Sensitive, "the visitor does not know there is one");
        }
    }
}
