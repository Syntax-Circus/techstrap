namespace TechStrap.Client;

/// <summary>Stable, machine-readable codes the client reports for a failed call. Match on these, not on message text.</summary>
public static class TechStrapClientErrorCodes
{
    /// <summary>An attachment could not be opened or read locally. Its path and exception details are never returned.</summary>
    public const string AttachmentReadFailed = "attachment-read-failed";

    /// <summary>More than five attachments were supplied.</summary>
    public const string AttachmentsTooMany = "attachments-too-many";
    /// <summary>Attachments together exceed 25 MiB.</summary>
    public const string AttachmentsTooLarge = "attachments-too-large";
    /// <summary>One attachment exceeds 10 MiB.</summary>
    public const string AttachmentTooLarge = "attachment-too-large";
    /// <summary>An attachment type is not allowed.</summary>
    public const string AttachmentTypeNotAllowed = "attachment-type-not-allowed";
    /// <summary>An attachment filename is invalid.</summary>
    public const string AttachmentNameInvalid = "attachment-name-invalid";
    /// <summary>An attachment MIME type cannot be parsed.</summary>
    public const string AttachmentContentTypeInvalid = "attachment-content-type-invalid";
    /// <summary>An attachment declares a negative size.</summary>
    public const string AttachmentSizeInvalid = "attachment-size-invalid";

    /// <summary>The API rejected the key (missing, unknown, revoked or not allowed for this product).</summary>
    public const string InvalidApiKey = "invalid-api-key";

    /// <summary>The API rejected the request body as invalid.</summary>
    public const string ValidationFailed = "validation-failed";

    /// <summary>The request body was larger than the API accepts.</summary>
    public const string PayloadTooLarge = "payload-too-large";

    /// <summary>The API does not accept the content type that was sent.</summary>
    public const string UnsupportedMediaType = "unsupported-media-type";

    /// <summary>The API is rate limiting this caller; retry later.</summary>
    public const string RateLimited = "rate-limited";

    /// <summary>The API could not be reached or answered with a server error after the retries were spent.</summary>
    public const string ApiUnavailable = "api-unavailable";

    /// <summary>The API answered with something the client does not understand.</summary>
    public const string UnexpectedResponse = "api-unexpected-response";

    /// <summary>The API reported an error that has no more specific code.</summary>
    public const string ApiError = "api-error";
}
