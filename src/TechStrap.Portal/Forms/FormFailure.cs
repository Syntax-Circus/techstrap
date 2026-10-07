using Microsoft.AspNetCore.Http;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// What a form shows when the API refused it (the contact form and the reply form), decided from the errors alone: the field errors for a 400 (the API's codes, the Portal's sentences), the attachment error for a
/// 413 or 415, a calm notice with a status for a rate limit, a conflict or an outage, and the uniform not-found when the product or ticket is gone. The visitor's text stays on the page in every case. Nothing the API
/// said is ever shown: the sentences are <see cref="FormCopy"/> and <see cref="ProblemCopy"/>.
/// </summary>
public sealed record FormFailure(IReadOnlyList<FormError> Errors, string? Notice, int Status, bool IsNotFound)
{
    /// <summary>The calm notice for a guarded write whose answer is unknown (it timed out): the same sentence and status as an outage.</summary>
    public static FormFailure Unknown { get; } = WithNotice(FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable);

    public static FormFailure From(IReadOnlyList<ResultError> errors)
    {
        if (errors.Any(error => error.Kind == ResultErrorKind.NotFound))
        {
            return new FormFailure([], null, StatusCodes.Status404NotFound, true);
        }

        var first = errors[0];
        return first.Code switch
        {
            ApiErrorCodes.RateLimited => WithNotice(FormCopy.RateLimited, StatusCodes.Status429TooManyRequests),
            ApiErrorCodes.ReplyConflict => WithNotice(ProblemCopy.ReplyConflict, StatusCodes.Status409Conflict),
            ApiErrorCodes.ApiUnavailable or ApiErrorCodes.UnexpectedResponse or ApiErrorCodes.ApiError => WithNotice(FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable),
            ApiErrorCodes.PayloadTooLarge or ApiErrorCodes.UnsupportedMediaType =>
                new FormFailure([new FormError(FormFields.Attachments, first.Code, FormCopy.For(first.Code))], null, StatusCodes.Status200OK, false),
            _ => new FormFailure([.. errors.Select(Field)], null, StatusCodes.Status200OK, false),
        };
    }

    private static FormFailure WithNotice(string text, int status) => new([], text, status, false);

    // An attachment code always belongs to the attachments field, whatever target the API gave it.
    private static FormError Field(ResultError error) => new(
        error.Code.StartsWith("attachment", StringComparison.Ordinal) ? FormFields.Attachments : FormFields.FromTarget(error.Target),
        error.Code,
        FormCopy.For(error.Code));
}
