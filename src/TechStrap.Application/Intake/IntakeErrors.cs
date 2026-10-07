using SyntaxCircus.Common;
using TechStrap.Contracts.Http;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Intake;

internal static class IntakeErrors
{
    public static ResultError ProductNotFound() =>
        new("product-not-found", "That product does not exist.", ResultErrorKind.NotFound);

    public static ResultError ApiKeyRequired() =>
        new("api-key-required", "Send a valid product API key to submit a ticket.", ResultErrorKind.Unauthenticated);

    /// <summary>Same code and target as the Domain's <c>Guard.RequiredText</c> failure for the message body.</summary>
    public static ResultError BodyTooLong() =>
        new("body-too-long", $"body must be at most {DomainLimits.MessageBodyMaxLength} characters.", ResultErrorKind.Validation, "body");

    public static ResultError TooManyFiles() =>
        new("attachments-too-many", $"Attach at most {Contracts.Intake.IntakeLimits.MaxFiles} files.", ResultErrorKind.Validation, "attachments");

    public static ResultError MessageTooLarge() =>
        new("attachments-too-large", "The attachments together are over 25 MiB. Remove a file or send smaller ones.", ResultErrorKind.Validation, "attachments");

    public static ResultError MetadataInvalid(string message) =>
        new("metadata-invalid", message, ResultErrorKind.Validation, "metadata");

    public static ResultError IdempotencyKeyInvalid() =>
        new("idempotency-key-invalid", $"The Idempotency-Key must be 1 to {Contracts.Intake.IntakeLimits.MaxIdempotencyKeyLength} characters.", ResultErrorKind.Validation, HeaderNames.IdempotencyKey);
}
