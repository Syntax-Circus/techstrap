using Microsoft.AspNetCore.Http;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>What a form shows when the API refused it, decided from the errors alone, in the Portal's own words (never the API's) and with the status a visitor and a monitor can use.</summary>
public sealed class FormFailureTests
{
    private static ResultError Validation(string code, string target, string message = "API TEXT") => new(code, message, ResultErrorKind.Validation, target);

    private static ResultError Failure(string code) => new(code, "API TEXT", ResultErrorKind.Failure);

    [Fact]
    public void Field_errors_keep_the_apis_codes_and_become_the_portals_sentences_on_the_matching_fields()
    {
        var failure = FormFailure.From([Validation("email-invalid", "email"), Validation("body-too-long", "Body"), Validation("subject-required", "subject")]);

        failure.Status.ShouldBe(StatusCodes.Status200OK);
        failure.Notice.ShouldBeNull();
        failure.IsNotFound.ShouldBeFalse();
        failure.Errors.Select(e => (e.Field, e.Code)).ShouldBe([(FormFields.Email, "email-invalid"), (FormFields.Body, "body-too-long"), (FormFields.Subject, "subject-required")]);
        failure.Errors.ShouldAllBe(e => !e.Message.Contains("API TEXT", StringComparison.Ordinal));
        failure.Errors[0].Message.ShouldBe("Enter a valid email address, like name@example.com.");
    }

    [Theory]
    [InlineData("attachments-too-many", "attachments")]
    [InlineData("attachments-too-large", "attachments")]
    [InlineData("attachment-too-large", "somewhere-else")]
    [InlineData("attachment-empty", null)]
    [InlineData("attachment-type-not-allowed", "attachment")]
    public void An_attachment_code_always_belongs_to_the_attachments_field(string code, string? target)
    {
        var failure = FormFailure.From([Validation(code, target!)]);

        failure.Errors.ShouldHaveSingleItem().Field.ShouldBe(FormFields.Attachments);
    }

    [Fact]
    public void An_unknown_code_or_target_is_the_generic_sentence_and_a_summary_item_without_a_link()
    {
        var failure = FormFailure.From([Validation("a-new-code", "mystery")]);

        var error = failure.Errors.ShouldHaveSingleItem();
        error.Field.ShouldBeNull();
        error.Message.ShouldBe(ProblemCopy.Invalid);
    }

    [Fact]
    public void A_rate_limit_is_a_429_with_a_calm_notice_and_no_field_errors()
    {
        var failure = FormFailure.From([Failure(ApiErrorCodes.RateLimited)]);

        failure.Status.ShouldBe(StatusCodes.Status429TooManyRequests);
        failure.Notice.ShouldBe(FormCopy.RateLimited);
        failure.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    [InlineData(ApiErrorCodes.UnexpectedResponse)]
    [InlineData(ApiErrorCodes.ApiError)]
    public void An_outage_or_an_unreadable_answer_is_a_503_with_the_unavailable_notice(string code)
    {
        var failure = FormFailure.From([Failure(code)]);

        failure.Status.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        failure.Notice.ShouldBe(FormCopy.Unavailable);
    }

    [Fact]
    public void A_reply_conflict_is_a_409_with_its_own_notice()
    {
        var failure = FormFailure.From([new ResultError(ApiErrorCodes.ReplyConflict, "API TEXT", ResultErrorKind.Conflict)]);

        failure.Status.ShouldBe(StatusCodes.Status409Conflict);
        failure.Notice.ShouldBe(ProblemCopy.ReplyConflict);
    }

    [Theory]
    [InlineData(ApiErrorCodes.PayloadTooLarge, "That is too large to send.")]
    [InlineData(ApiErrorCodes.UnsupportedMediaType, "That could not be sent in that form.")]
    public void A_413_or_a_415_is_an_attachment_error_on_a_normal_page(string code, string sentence)
    {
        var failure = FormFailure.From([Failure(code)]);

        failure.Status.ShouldBe(StatusCodes.Status200OK);
        var error = failure.Errors.ShouldHaveSingleItem();
        error.Field.ShouldBe(FormFields.Attachments);
        error.Message.ShouldStartWith(sentence);
    }

    [Fact]
    public void A_not_found_anywhere_in_the_errors_is_the_uniform_404()
    {
        var failure = FormFailure.From([new ResultError(ApiErrorCodes.NotFound, "API TEXT", ResultErrorKind.NotFound)]);

        failure.IsNotFound.ShouldBeTrue();
        failure.Status.ShouldBe(StatusCodes.Status404NotFound);
        failure.Errors.ShouldBeEmpty();
        failure.Notice.ShouldBeNull();
    }
}
